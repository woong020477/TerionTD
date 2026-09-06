using UnityEngine;

/// <summary>타워 건설 슬롯의 소유자·네트워크 ID와 선택 동작을 관리합니다.</summary>
public class TowerBase : MonoBehaviour, ITowerSelectable
{
    public PlayerController Owner { get; private set; }
    public int NetId { get; private set; }

    [HideInInspector]
    public TowerType towerType; // 타워 종류를 저장하는 변수
    [Header("베이스 정보")] // 타워 베이스에 정보가 없으면 Null떠서 넣음;;
    public string baseDisplayName = "타워 베이스"; // 기본 이름
    public float baseDamage = 0f; // 기본 공격력 (없음)
    public float baseAttackDelay = 0f; // 기본 발사지연 (없음)
    [Header("타워 프리팹")]
    [SerializeField]
    GameObject FramethrowerTower;
    [SerializeField]
    GameObject LaserTower;
    [SerializeField]
    GameObject MachineGunTower;
    [SerializeField]
    GameObject RocketLauncherTower;
    [SerializeField]
    GameObject MultipleRocketTower;
    [SerializeField]
    GameObject BuffCube;
    public void SetNetId(int id) => NetId = id;
    public void Initialize(PlayerController player, int netId)
    {
        Owner = player;
        NetId = netId;
    }

    private GameObject SpawnTowerInternal(TowerType type)
    {
        GameObject prefab = null;
        Vector3 spawnPos = transform.position;
        switch (type)
        {
            case TowerType.Flame:
                prefab = FramethrowerTower;
                break;
            case TowerType.Laser:
                prefab = LaserTower;
                spawnPos.y += 0.1f;
                break;
            case TowerType.Machine:
                prefab = MachineGunTower;
                break;
            case TowerType.Rocket:
                prefab = RocketLauncherTower;
                break;
            case TowerType.Multiple:
                prefab = MultipleRocketTower;
                spawnPos.y += 0.1f;
                break;
        }

        if (prefab == null)
            return null;
        towerType = type;
        var newTower = Instantiate(prefab, spawnPos, Quaternion.identity, transform);
        if (Owner != null && newTower.TryGetComponent<TowerController>(out var towerCtrl))
        {
            // 베이스와 동일한 로컬 ID를 사용하되 소유자 인덱스를 결합해 매치 내에서 고유하게 만든다.
            towerCtrl.InitializeNetwork(Owner.PlayerIndex, NetId, Owner);
            towerCtrl.towerType = type;
            int lvl = UIManager.Instance.GetLabLevel(Owner.PlayerIndex, type);
            towerCtrl.upgradeLevel = Mathf.Max(1, lvl);
            towerCtrl.ApplyTowerStats(towerCtrl.upgradeLevel);
        }

        if (Owner != null && Owner.IsLocalPlayer)
        {
            UIManager.Instance.CloseBuildingStatus();
            UIManager.Instance.UpdateTowerUpgradePanel();
        }

        return newTower;
    }

    // 타워 자식이 있는지 검사
    private bool HasTowerChild()
    {
        foreach (Transform child in transform)
        {
            if (child.gameObject.layer == 9)
                return true;
            if (child.gameObject.layer == 13)
                return false;
        }

        return false;
    }

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
        if (newTower == null)
            return;
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

    public void CreateTowerRemote(TowerType type, int level)
    {
        if (HasTowerChild())
            return; // 중복 방지
        var go = SpawnTowerInternal(type);
        if (go != null && go.TryGetComponent<TowerController>(out var tc))
        {
            tc.towerType = type;
            tc.upgradeLevel = level;
            tc.ApplyTowerStats(level);
        }
    }

    public void OnSelected()
    {
        var towerCtrl = GetComponentInChildren<TowerController>();
        bool isOwnedByLocal = Owner != null && Owner.IsLocalPlayer;
        // 다른 플레이어의 빈 베이스는 건설/업그레이드 진입점으로 취급하지 않습니다.
        if (!isOwnedByLocal && towerCtrl == null)
            return;
        if (towerCtrl != null)
        {
            UIManager.Instance.UpdateBuildingStatus(towerCtrl, this);
            if (isOwnedByLocal)
                UIManager.Instance.SetUpgradeUI(Owner.PlayerIndex, false);
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
