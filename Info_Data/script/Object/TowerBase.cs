using UnityEngine;

public class TowerBase : MonoBehaviour, ITowerSelectable
{
    public PlayerController Owner { get; private set; }
    public int NetId { get; private set; }                                                                  // 네트워크 ID (서버에서 할당)

    [HideInInspector]
    public TowerType towerType;                                                                             // 타워 종류를 저장하는 변수

    [Header("베이스 정보")]                                                                                 // 타워 베이스에 정보가 없으면 Null떠서 넣음;;
    public string baseDisplayName = "타워 베이스";                                                          // 기본 이름
    public float baseDamage = 0f;                                                                           // 기본 공격력 (없음)
    public float baseAttackDelay = 0f;                                                                      // 기본 발사지연 (없음)

    [Header("타워 프리팹")]
    [SerializeField] GameObject FramethrowerTower;
    [SerializeField] GameObject LaserTower;
    [SerializeField] GameObject MachineGunTower;
    [SerializeField] GameObject RocketLauncherTower;
    [SerializeField] GameObject MultipleRocketTower;
    [SerializeField] GameObject BuffCube;

    public void SetNetId(int id) => NetId = id;

    public void Initialize(PlayerController player, int netId)                                                         // PlayerController에서 호출
    {
        Owner = player;
        NetId = netId;
    }

    // ----------------- 공통 스폰 로직(실제 Instantiate) -----------------
    private GameObject SpawnTowerInternal(TowerType type)
    {
        GameObject prefab = null;
        Vector3 spawnPos = transform.position;

        switch (type)
        {
            case TowerType.Flame: prefab = FramethrowerTower; break;
            case TowerType.Laser: prefab = LaserTower; spawnPos.y += 0.1f; break;
            case TowerType.Machine: prefab = MachineGunTower; break;
            case TowerType.Rocket: prefab = RocketLauncherTower; break;
            case TowerType.Multiple: prefab = MultipleRocketTower; spawnPos.y += 0.1f; break;
        }
        if (prefab == null) return null;

        towerType = type;

        var newTower = Instantiate(prefab, spawnPos, Quaternion.identity, transform);

        if (Owner != null && newTower.TryGetComponent<TowerController>(out var towerCtrl))
        {
            towerCtrl.SetOwner(Owner);
            towerCtrl.towerType = type;

            int lvl = UIManager.Instance.GetLabLevel(Owner.PlayerIndex, type);
            towerCtrl.upgradeLevel = Mathf.Max(1, lvl);
            towerCtrl.ApplyTowerStats(towerCtrl.upgradeLevel);
        }
            

        UIManager.Instance.CloseBuildingStatus();
        UIManager.Instance.UpdateTowerUpgradePanel();

        return newTower;
    }

    // 타워 자식이 있는지 검사
    private bool HasTowerChild()
    {
        foreach (Transform child in transform)
        {
            if (child.gameObject.layer == 9) return true; // "Tower" 레이어
            if (child.gameObject.layer == 13) return false; // "Select" 레이어
        }
        return false;
    }

    // ----------------- 로컬에서 타워 생성 -----------------
    public void CreateTower(TowerType type)
    {
        long cost = (long)UIManager.Instance.GetBuildCost(type);
        if (!Owner.UseGold(cost))
        {
            Debug.LogWarning($"골드가 부족해 {type} 타워를 건설할 수 없습니다.");
            return;
        }
        if (HasTowerChild())
        {
            Debug.LogWarning("이미 타워가 존재하여 생성하지 않음");
            return;
        }

        var newTower = SpawnTowerInternal(type);
        if (newTower == null) return;

        // 로컬 플레이어만 네트워크 통지
        if (Owner != null && Owner.IsLocalPlayer && UDPClient.Instance != null)
        {
            var msg = new TowerCreateMessage
            {
                ownerIndex = Owner.PlayerIndex,
                baseNetId = NetId,
                towerType = type
            };
            UDPClient.Instance.SendUDP(JsonUtility.ToJson(msg), "TOWER_CREATE");
        }
    }

    // ----------------- 원격 재현(골드/검사 X, 한 번만) -----------------
    public void CreateTowerRemote(TowerType type, int level)
    {
        if (HasTowerChild()) return; // 중복 방지

        var go = SpawnTowerInternal(type);
        if (go != null && go.TryGetComponent<TowerController>(out var tc))
        {
            tc.towerType = type;
            tc.upgradeLevel = level;
            tc.ApplyTowerStats(level);
        }
    }

    // ----------------- ITowerSelectable 인터페이스 구현 -----------------
    public void OnSelected()
    {
        var towerCtrl = GetComponentInChildren<TowerController>();
        bool isOwnedByLocal = Owner != null && Owner.IsLocalPlayer;

        if (towerCtrl != null)
        {
            UIManager.Instance.UpdateBuildingStatus(towerCtrl, this);
            if (isOwnedByLocal) UIManager.Instance.SetUpgradeUI(Owner.PlayerIndex, false);
        }
        else
        {
            UIManager.Instance.UpdateBuildingStatus(null, this);
            if (isOwnedByLocal)
            { 
                UIManager.Instance.SetUpgradeUI(Owner.PlayerIndex, true); 
                UIManager.Instance.ToggleSelectTowerPanel(true);
            }
        }
    }

    public void OnDeSelected()
    {
        if (UIManager.Instance != null)
        {
            UIManager.Instance.CloseBuildingStatus();
            if (Owner != null && Owner.IsLocalPlayer)
            {
                UIManager.Instance.SetUpgradeUI(Owner.PlayerIndex, false);
                UIManager.Instance.CloseBuildingStatus();
                UIManager.Instance.ToggleSelectTowerPanel(false);
            }
        }
    }

    private void OnDestroy()
    {
        // 소유자 로컬 여부와 무관하게 레지스트리 정리
        BuildingSystem.Instance?.UnregisterBase(this);
    }

    public void OnCreateFlameTower() 
    {
        CreateTower(TowerType.Flame); 
        Debug.Log("화염 타워 생성 요청");
    }
    public void OnCreateLaserTower() 
    {
        CreateTower(TowerType.Laser);
        Debug.Log("레이저 타워 생성 요청");
    }
    public void OnCreateMachineTower() 
    {
        CreateTower(TowerType.Machine);
        Debug.Log("기관총 타워 생성 요청");
    }
    public void OnCreateMultipleTower() 
    {
        CreateTower(TowerType.Multiple);
        Debug.Log("다연장 로켓 타워 생성 요청");
    } 
    public void OnCreateRocketTower()
    {
        CreateTower(TowerType.Rocket);
        Debug.Log("로켓 타워 생성 요청");
    }
}
