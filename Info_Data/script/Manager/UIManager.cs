using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 플레이어 UI 설정 클래스
[System.Serializable]
public class PlayerUISet
{
    public GameObject BuildModeUI;
    public GameObject UpgradeUI;
    public GameObject TowerLabUI;
    public GameObject GuidePanel;
    public TextMeshProUGUI GoldText;

    public PlayerController controller;
}
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }
    private readonly Dictionary<int, Dictionary<TowerType, int>> playerLabUpgradeCounts             // 플레이어별 타워 연구소 업그레이드 횟수 딕셔너리
    = new Dictionary<int, Dictionary<TowerType, int>>();

    [Header("UI 패널")]
    public GameObject BuildModeUI;                              // 빌드모드 UI
    public GameObject UpgradeUI;                                // 업그레이드 패널
    public GameObject EnemyInfoUI;                              // 적 정보 패널
    public GameObject TowerLabUI;                               // 타워 연구소 UI
    public GameObject LeaderBoardUI;                            // 리더보드 UI
    public GameObject BuildingStatusPanel;                      // 타워 상태 패널
    public GameObject TowerSelectPanel;                         // 타워 선택 패널
    public GameObject AbilityInfoPanel;                         // 능력 정보 패널
    public GameObject AttributeInfoPanel;                       // 적 속성 정보 패널
    public GameObject GuidePanel;                               // 가이드 패널
    public GameObject ExitPanel;                                // 게임 종료 패널
    public GameObject SelectTowerPanel;                         // 타워 선택 패널

    [Header("플레이어 상태 UI")]
    public TMP_Text MyStatus_GoldText;

    [Header("적 정보 UI")]
    [SerializeField] private TMP_Text EnemyName;
    [SerializeField] private TMP_Text EnemyHP;
    [SerializeField] private TMP_Text EnemyAttribute;
    [SerializeField] private TMP_Text EnemyArmor;
    [SerializeField] private TMP_Text AttributeText;            // 속성 설명 텍스트

    [Header("건물 정보 UI")]
    public TMP_Text TowerNameText;                              // 타워 이름 텍스트
    public TMP_Text DamageText;                                 // 공격력 텍스트
    public TMP_Text AttackSpeedText;                            // 발사지연 텍스트
    public Image AbilityImage;                                  // 능력 이미지
    public TMP_Text AbilityText;                                // 능력 설명 텍스트
    public Sprite[] AbilityInputImage;                           // 능력 입력 이미지 리스트

    [Header("리더보드 버튼")]
    [SerializeField] private GameObject LeaderBoard_Button_UpButton;
    [SerializeField] private GameObject LeaderBoard_Button_DownButton;

    [Header("타워 업그레이드 버튼")]
    [SerializeField] private GameObject FlameThrowerTowerButton;
    [SerializeField] private GameObject LaserToweButton;
    [SerializeField] private GameObject MachineGunTowerButton;
    [SerializeField] private GameObject MultipleRocketLuncherTowerButton;
    [SerializeField] private GameObject RocketTowerButton;

    [Header("타워 연구소 버튼")]
    [SerializeField] private GameObject FlameThrowerTower_Lab_Button;
    [SerializeField] private GameObject LaserTower_Lab_Button;
    [SerializeField] private GameObject MachineGunTower_Lab_Button;
    [SerializeField] private GameObject MultipleRocketLuncherTower_Lab_Button;
    [SerializeField] private GameObject RocketTower_Lab_Button;


    [Header("배속 버튼")]
    [SerializeField] private TextMeshProUGUI xButtonText;

    private string Enemy_HP_Gradient;                                                                       // 적 HP 그라데이션 색상 프리셋 불러올 때 쓰는 문자열
    private TowerBase currentTower;                                                                         // 현재 선택된 타워(터렛베이스)가 뭔지 알아낼때 씀
    [HideInInspector] public PlayerController owner;                                                        // 현재 타워 연구소(플레이어 컨트롤러)
    private Enemy currentEnemy;                                                                             // 현재 선택된 적(Enemy) 객체

    public bool guidePanelflag = false;
    private LayerMask worldClickMask = ~0;
    private int attackRangeLayer;

    private bool isMoveMode;
    private Coroutine moveRoutine;

    private static readonly Dictionary<TowerType, float> TowerBuildCosts = new Dictionary<TowerType, float> // 타워 종류(Enum)별 건설 비용 구조체
    {
        { TowerType.Flame, 50f },
        { TowerType.Laser, 50f },
        { TowerType.Machine, 200f },
        { TowerType.Rocket, 300f },
        { TowerType.Multiple, 200f }
    };

    private Dictionary<TowerType, int> towerBuildCounts = new Dictionary<TowerType, int>();                 // 타워 종류별 건설 횟수를 저장하는 딕셔너리

    public List<PlayerUISet> playerUIs = new List<PlayerUISet>();                                           // 최대 4개

    private double _lastHp = double.NaN;                                                                    // 마지막으로 갱신된 적 HP
    private double _lastMaxHp = double.NaN;                                                                 // 마지막으로 갱신된 적 최대 HP
    public void Initialize(PlayerController controller)
    {
        owner = controller;
    }

    public int LocalPlayerIndex => owner ? owner.PlayerIndex : -1;                                          // 로컬 플레이어 인덱스 반환
    private bool IsReady() => owner != null && owner.IsLocalPlayer && LocalPlayerIndex >= 0;                // 로컬 플레이어가 준비되었는지 확인하는 함수

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        attackRangeLayer = LayerMask.NameToLayer("TowerAttack");
        if (attackRangeLayer >= 0)
            worldClickMask &= ~(1 << attackRangeLayer);          // AttackRange 레이어 제외
    }
    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
    void Start()
    {
        BuildModeUI.SetActive(false);
        UpgradeUI.SetActive(false);
        EnemyInfoUI.SetActive(false);
        TowerLabUI.SetActive(false);
        BuildingStatusPanel.SetActive(false);
        LeaderBoardUI.SetActive(false);
        GuidePanel.SetActive(false);
        UpdateTowerUpgradePanel();
    }
    private void Update()
    {
        // 타워 상태 패널 클릭 처리
        HandleClickBuildingStatus();

        // SelectTowerPanel 활성 + C키 → 이동 모드 진입
        if (SelectTowerPanel.activeSelf && Input.GetKeyDown(KeyCode.C))
        {
            MoveSelectedTower();
        }

        // 선택된 적이 없거나 파괴된 경우 UI 닫기
        if (currentEnemy == null || currentEnemy.Equals(null))
        {
            if (EnemyInfoUI.activeSelf)
            {
                ShutdownEnemyInfoUI();
            }
            return;
        }

        // 적 정보 UI 갱신
        if (EnemyInfoUI.activeSelf)
        {
            double hp = currentEnemy.HP;
            double maxHp = currentEnemy.MaxHP;

            if (!hp.Equals(_lastHp) || !maxHp.Equals(_lastMaxHp))
            {
                UpdateEnemyHPGradient(currentEnemy);
                EnemyHP.text = $"<gradient={Enemy_HP_Gradient}>{hp} / {maxHp}</gradient>";
                _lastHp = hp; _lastMaxHp = maxHp;
            }
        }
    }

    public bool IsMoveModeActive => isMoveMode;
    public void ForceEndMoveMode()
    {
        if (!isMoveMode) return;
        isMoveMode = false;
        BuildingSystem.Instance.EndGrid(this); // 예약 해제
        if (moveRoutine != null) { StopCoroutine(moveRoutine); moveRoutine = null; }
    }

    public void OnClickMoveSelectedTower()
    {
        MoveSelectedTower();
    }

    // 선택된 타워 이동 모드 진입
    private void MoveSelectedTower()
    {
        if (isMoveMode) return;
        if (currentTower == null) return;                              // TowerBase (선택 중 베이스)
        if (currentTower.Owner == null || !currentTower.Owner.IsLocalPlayer) return;


        owner?.CancelBuildModeIfActive();

        if (!BuildingSystem.Instance.BeginGrid(BuildingSystem.GridOwner.Move, this))
        {
            Debug.Log("[Move] 건설 중에는 이동을 시작할 수 없습니다.");
            return;
        }

        isMoveMode = true;
        moveRoutine = StartCoroutine(MoveSelectedTowerRoutine(currentTower));
    }

    // 좌클릭으로 확정 / 우클릭/ESC로 취소
    private IEnumerator MoveSelectedTowerRoutine(TowerBase baseComp)
    {
        // 이동 대상 및 기존 Y 기억
        var tc = baseComp.GetComponentInChildren<TowerController>(true);
        float baseY = baseComp.transform.position.y;
        float towerY = tc ? tc.transform.position.y : 0f;

        while (isMoveMode)
        {
            // 취소: 우클릭 또는 ESC
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            {
                isMoveMode = false;
                BuildingSystem.Instance.EndGrid(this);
                yield break;
            }

            // 좌클릭 확정 (UI 위 클릭 제외)
            if (Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject())
            {
                // 설치 가능 칸만 허용(빨강일 때 무시)
                if (BuildingSystem.Instance.IsBuildableNow)
                {
                    Vector3 aligned = BuildingSystem.Instance.CurrentAlignedCursorPosition();

                    // XZ만 이동, Y는 각자 기존값 유지
                    Vector3 newBasePos = new Vector3(aligned.x, baseY, aligned.z);
                    Vector3 newTowerPos = tc ? new Vector3(aligned.x, towerY, aligned.z) : Vector3.zero;

                    // 실제 이동(로컬)
                    baseComp.transform.position = newBasePos;
                    if (tc) tc.transform.position = newTowerPos;

                    // 네트워크 통보
                    UDPClient.Instance?.SendUDP(JsonUtility.ToJson(new TowerMoveMessage
                    {
                        ownerIndex = owner ? owner.PlayerIndex : -1,
                        baseNetId = baseComp.NetId,
                        basePosition = newBasePos,
                        hasTower = (tc != null),
                        towerPosition = newTowerPos
                    }), "TOWER_MOVE");

                    SoundManager.Instance.PlaySFX(SoundKey.Build); // 확정 사운드

                    isMoveMode = false;
                    BuildingSystem.Instance.EndGrid(this);
                    break;
                }
            }
            yield return null;
        }
        moveRoutine = null;
    }

    public void RegisterPlayerUI(int index, PlayerController controller)
    {
        // 자동 확장
        while (index >= playerUIs.Count)
            playerUIs.Add(new PlayerUISet());

        playerUIs[index].controller = controller;

        var uiSet = playerUIs[index];
        uiSet.BuildModeUI?.SetActive(false);
        uiSet.UpgradeUI?.SetActive(false);
        uiSet.TowerLabUI?.SetActive(false);
        uiSet.GuidePanel?.SetActive(false);

        UpdateGoldUI(index);
    }

    // 골드 UI 갱신 함수
    public void UpdateGoldUI(int index)
    {
        if (index < 0 || index >= playerUIs.Count) return;

        var playerSet = playerUIs[index];
        if (playerSet.controller == null || playerSet.GoldText == null) return;

        playerSet.GoldText.text = $"{playerSet.controller.GetGold():N0} G";
    }

    private void HandleClickBuildingStatus()
    {
        // 패널이 꺼져 있으면 검사 불필요
        if (!BuildingStatusPanel || !BuildingStatusPanel.activeSelf) return;

        // 마우스 왼쪽 클릭 && UI 위가 아니어야 함
        if (Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject())
        {
            var cam = Camera.main;
            if (!cam) return;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, 2000f, worldClickMask, QueryTriggerInteraction.Ignore))
            {
                // 바닥/적/타워 외 오브젝트 클릭이면 닫기
                var selectable = hit.collider.GetComponentInParent<ITowerSelectable>();
                if (selectable == null)
                {
                    CloseBuildingStatus(); // 패널 OFF
                    ToggleSelectTowerPanel(false); // 가이드 패널 OFF
                }
                    
            }
            else
            {
                // 아무것도 안 맞았으면 빈 공간 클릭 → 닫기
                CloseBuildingStatus();
                ToggleSelectTowerPanel(false); // 가이드 패널 OFF
                if (owner != null) SetUpgradeUI(owner.PlayerIndex, false);
            }
        }
    }
    public void ToggleSelectTowerPanel(bool selectTowerFlag)                                                                   // 가이드 패널 토글 함수
    {
        SelectTowerPanel.SetActive(selectTowerFlag);
    }

    // 타워 생성 버튼 클릭 이벤트 핸들러
    public void OnCreateTowerButtonClicked(TowerType TowerType)                 // 버튼이랑 매칭시키는 함수 (버튼 인자전달 부분에 생성 터렛이름 넣어야함)
    {
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
        if (currentTower != null)
        {
            currentTower.CreateTower(TowerType);
        }
        else
        {
            Debug.LogWarning("선택된 타워 없음");
        }
        if (owner != null)
        {
            int playerIndex = owner.PlayerIndex;
            SetUpgradeUI(playerIndex, false);
        }

    }

    public void OnClickOptionButton()
    {
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
        // 옵션 버튼 클릭 시 OptionManager의 OnClickOpen 호출
        OptionManager.Instance.OnClickOpen();
    }

    private void UpdateEnemyHPGradient(Enemy currentEnemy)                                          // 적의 HP 퍼센트에 따른 Gradient 적용
    {
        // 현재 HP 비율 계산 (0 ~ 1)
        double hpPercent = currentEnemy.HP / currentEnemy.MaxHP;
        
        // HP 비율에 따라 Gradient 값 지정
        if (hpPercent <= 0.25f)
        {
            Enemy_HP_Gradient = "Enemy_HP25";
        }
        else if (hpPercent <= 0.50f)
        {
            Enemy_HP_Gradient = "Enemy_HP50";
        }
        else if (hpPercent <= 0.75f)
        {
            Enemy_HP_Gradient = "Enemy_HP75";
        }
        else
        {
            Enemy_HP_Gradient = "Enemy_HP100";
        }
    }

    public void UpdateBuildingStatus(TowerController tower, TowerBase baseInfo = null)              // 타워 상태 패널 업데이트 메소드
    {
        BuildingStatusPanel.SetActive(true);

        currentTower = baseInfo;                                                                    // 현재 타워 베이스 정보 저장
        // 타워 이름 출력
        if (tower == null && baseInfo != null)
        {
            // 타워가 없고 베이스만 선택한 경우
            TowerNameText.text = baseInfo.baseDisplayName;
            DamageText.text = "공격 불가";
            AttackSpeedText.text = "";
            AbilityImage.sprite = AbilityInputImage[6];
            AbilityText.text = "능력 없음\n\n타워 발판에 능력이 있을거라고 생각하십니까...?";
            return;
        }
        else
        {
            TowerNameText.text = tower.towerDisplayName;
            DamageText.text = $"공격력 : {tower.damage}";
            AttackSpeedText.text = $"발사지연(초) : {tower.attackDelay}";
            switch (tower.towerType)
            {
                case TowerType.Flame:
                    AbilityImage.sprite = AbilityInputImage[0];
                    AbilityText.text = "점화\n\n빠른 속도로 적을 불태우며 <color=green>10%</color>확률로 적 방어를 <color=red>관통</color>합니다.";
                    break;

                case TowerType.Laser:
                    AbilityImage.sprite = AbilityInputImage[1];
                    AbilityText.text = "광증폭\n\n빠른 속도로 적을 공격하며 <color=green>10%</color>확률로 적 방어를 <color=red>관통</color>합니다.";
                    break;

                case TowerType.Machine:
                    AbilityImage.sprite = AbilityInputImage[2];
                    AbilityText.text = "집중 사격\n\n빠른 속도로 적을 연속 공격하며 <color=green>15%</color>확률로 <color=#68DEAB>2초</color> 기절합니다.";
                    break;

                case TowerType.Multiple:
                    AbilityImage.sprite = AbilityInputImage[4];
                    AbilityText.text = "무차별 폭격\n\n공격 시 범위 공격하며 <color=green>15%</color>확률로 <color=#68DEAB>3초</color> 기절합니다.";
                    break;

                case TowerType.Rocket:
                    AbilityImage.sprite = AbilityInputImage[3];
                    AbilityText.text = "비장의 한 발\n\n공격 시 범위 공격하며 적 방어를 <color=red>관통</color>합니다.";
                    break;
            }
        }
    }

    public void ShowAbilityInfo()                                                                   // 능력 정보 패널 열기 이벤트 핸들러
    {
        if (AbilityInfoPanel != null)
        {
            AbilityInfoPanel.SetActive(true);
        }
    }

    public void HideAbilityInfo()                                                                   // 능력 정보 패널 닫기 이벤트 핸들러
    {
        if (AbilityInfoPanel != null)
        {
            AbilityInfoPanel.SetActive(false);
        }
    }

    public void ShowAttributeInfo()                                                                   // 능력 정보 패널 열기 이벤트 핸들러
    {
        if (AttributeInfoPanel != null)
        {
            AttributeInfoPanel.SetActive(true);
        }
    }

    public void HideAttributeInfo()                                                                   // 능력 정보 패널 닫기 이벤트 핸들러
    {
        if (AttributeInfoPanel != null)
        {
            AttributeInfoPanel.SetActive(false);
        }
    }
    public void UpdateBuildingStatus_Base(TowerBase baseInfo)                                       // 타워 상태 패널 업데이트 메소드 (타워가 아닌 베이스 정보만 있을 때)
    {
        if (baseInfo == null)
        {
            BuildingStatusPanel.SetActive(false);
            return;
        }

        BuildingStatusPanel.SetActive(true);

        // 이름
        TowerNameText.text = baseInfo.baseDisplayName;

        // 공격력
        DamageText.text = "공격 불가";

        // 발사지연
        AttackSpeedText.text = "";
    }

    public void CloseBuildingStatus()                                                               // 타워 상태 패널 닫기 메소드
    {
        BuildingStatusPanel.SetActive(false);
        AbilityInfoPanel.SetActive(false);
    }

    public void IncreaseTowerBuildCount(TowerType type)                                             // 타워 종류별 건설 횟수 증가 메소드
    {
        if (!towerBuildCounts.ContainsKey(type))
            towerBuildCounts[type] = 0;

        towerBuildCounts[type]++;
    }

    public float GetBuildCost(TowerType type)                                                       // 타워 종류에 따른 건설 비용 계산 메소드
    {
        if (!towerBuildCounts.ContainsKey(type))
            towerBuildCounts[type] = 0;

        int count = towerBuildCounts[type];
        float baseCost = TowerBuildCosts[type];
        float multiplier;

        if (count < 45)
        {
            // 초반 15개까지는 가파르게 1.7배씩 상승
            multiplier = Mathf.Pow(1.7f, count);
        }
        else
        {
            // 45개 이후부터는 완화된 상승 (1.2배)
            multiplier = Mathf.Pow(1.7f, 45) * Mathf.Pow(1.2f, count - 45);
        }

        // 상한선 3배 제한
        return baseCost * multiplier;
    }

    private void UpdateTowerPriceText(GameObject buttonObj, TowerType type)                         // 타워 업그레이드 버튼의 가격 텍스트 업데이트
    {
        if (buttonObj == null) return;

        TMP_Text[] texts = buttonObj.GetComponentsInChildren<TMP_Text>(true);
        TMP_Text priceText = null;

        foreach (var txt in texts)
        {
            if (txt.name == "TowerPrice")
                priceText = txt;
        }

        // 기본 비용
        float baseCost = TowerBuildCosts[type];

        // 동적 비용 계산
        float dynamicCost = GetBuildCost(type);

        // UI 적용
        priceText.text = $"{dynamicCost:0} G";
    }

    // 플레이어 연구 레벨 가져오기
    public int GetLabLevel(int playerIndex, TowerType towerType)
    {
        if (!playerLabUpgradeCounts.TryGetValue(playerIndex, out var map))
        {
            map = new Dictionary<TowerType, int>();
            foreach (TowerType tt in System.Enum.GetValues(typeof(TowerType))) map[tt] = 1; // 기본 1
            playerLabUpgradeCounts[playerIndex] = map;
        }
        return map[towerType];
    }

    // 플레이어 연구 레벨 설정하기
    public void SetLabLevel(int playerIndex, TowerType t, int level)
    {
        var _ = GetLabLevel(playerIndex, t); // 초기화 보장
        playerLabUpgradeCounts[playerIndex][t] = level;
    }

    // 타워 연구 실행
    public void TryUpgradeLab(int index, TowerType towerType)
    {
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
        if (index < 0 || index >= playerUIs.Count) return;
        var player = playerUIs[index].controller;
        if (player == null) return;
        if (!player.IsLocalPlayer) return;                 // 로컬(오너)만 연구 실행

        TowerController.LoadUpgradeData();
        int currentLevel = GetLabLevel(index, towerType);
        var nextData = TowerController.StaticGetUpgradeData(currentLevel + 1);
        if (nextData == null) { Debug.Log("최대 레벨입니다."); return; }

        long cost = (long)TowerController.StaticGetTowerCost(nextData, towerType);
        if (!player.UseGold(cost)) { Debug.LogWarning("골드가 부족합니다."); return; }

        // 1) 내 연구 레벨 갱신
        int newLevel = currentLevel + 1;
        SetLabLevel(index, towerType, newLevel);
        Debug.Log($"[연구실] P{index + 1} {towerType} → Lv.{newLevel}");

        // 2) 내 소유 타워만 반영
        foreach (var tower in TowerController.Towers)
        {
            if (tower == null) continue;
            if (tower.Owner == player && tower.towerType == towerType)
            {
                tower.upgradeLevel = newLevel;
                tower.ApplyTowerStats(newLevel);
                UpdateBuildingStatus(tower);
            }
        }

        UpdateLabButtons();

        // 3) 네트워크 브로드캐스트(모든 클라가 UI/타워 상태 맞춤)
        UDPClient.Instance?.SendUDP(JsonUtility.ToJson(new TowerLabUpgradeMessage
        {
            ownerIndex = index,
            towerType = towerType,
            level = newLevel
        }), "TOWER_LAB_UPGRADE");
    }

    private void UpdateTowerLabButton(GameObject buttonObj, TowerType towerType)
    {
        if (buttonObj == null) return;

        TMP_Text priceText = null, countText = null;
        foreach (var txt in buttonObj.GetComponentsInChildren<TMP_Text>(true))
        {
            if (txt.name == "TowerPrice") priceText = txt;
            else if (txt.name == "UpgradeCount") countText = txt;
        }

        // 현재 UI의 주인(로컬 플레이어 인덱스) 기준으로 표시
        int idx = LocalPlayerIndex;
        int currentLevel = GetLabLevel(idx, towerType);
        var nextData = TowerController.StaticGetUpgradeData(currentLevel + 1);
        float cost = (nextData != null) ? TowerController.StaticGetTowerCost(nextData, towerType) : 0f;

        if (priceText) priceText.text = (nextData != null) ? $"{cost}G" : "MAX";
        if (countText) countText.text = $"{currentLevel}회";
    }

    // 타워 업그레이드 버튼 프리팹 가격 텍스트 업데이트
    public void UpdateTowerUpgradePanel()
    {
        UpdateTowerPriceText(FlameThrowerTowerButton, TowerType.Flame);
        UpdateTowerPriceText(LaserToweButton, TowerType.Laser);
        UpdateTowerPriceText(MachineGunTowerButton, TowerType.Machine);
        UpdateTowerPriceText(MultipleRocketLuncherTowerButton, TowerType.Multiple);
        UpdateTowerPriceText(RocketTowerButton, TowerType.Rocket);
    }

    // 타워 연구 버튼 프리팹 가격 및 몇강했는지 텍스트 업데이트
    public void UpdateLabButtons()
    {
        UpdateTowerLabButton(FlameThrowerTower_Lab_Button, TowerType.Flame);
        UpdateTowerLabButton(LaserTower_Lab_Button, TowerType.Laser);
        UpdateTowerLabButton(MachineGunTower_Lab_Button, TowerType.Machine);
        UpdateTowerLabButton(MultipleRocketLuncherTower_Lab_Button, TowerType.Multiple);
        UpdateTowerLabButton(RocketTower_Lab_Button, TowerType.Rocket);
    }

    public void SetBuildModeUI(int index, bool active)
    {
        if (index < 0 || index >= playerUIs.Count) return;
        playerUIs[index].BuildModeUI?.SetActive(active);
    }

    public void SetUpgradeUI(int index, bool active)
    {
        if (index < 0 || index >= playerUIs.Count) return;
        playerUIs[index].UpgradeUI?.SetActive(active);
    }

    public void ShutdownUpgradeUI()                                         // 인스펙터에선 bool 입력이 안되서 업그레이드 패널 끄기버튼용 함수
    {
        int index = LocalPlayerIndex;
        if (index < 0 || index >= playerUIs.Count) return;
        playerUIs[index].UpgradeUI?.SetActive(false);
    }

    public void ShowEnemyInfoUI(Enemy enemy)                                // 적 정보 패널 켜기 (* 현재 선택한 적이 뭔지 선택함)
    {
        currentEnemy = enemy;
        EnemyInfoUI.SetActive(true);
        switch (enemy.EnemyType)                                            // 적 타입에 따라 색상 변경
        {
            case EnemyType.HealthRegen:
                EnemyAttribute.text = $"<gradient=Enemy_HealthRegen>재생</gradient>";
                AttributeText.text = "재생\n\n매<color=#68DEAB>5초</color>마다 최대 체력의 <color=green>2%</color> 회복합니다.";
                break;
            case EnemyType.Invincible:
                EnemyAttribute.text = $"<gradient=Enemy_Invincible>무적</gradient>";
                AttributeText.text = "무적\n\n5초마다 <color=#68DEAB>2초</color>간 타워의 공격을 <color=green>100%</color>무시합니다.";
                break;
            case EnemyType.Armor:
                EnemyAttribute.text = $"<gradient=Enemy_Armor>철갑</gradient>";
                AttributeText.text = "철갑\n\n타워의 공격을 <color=green>50%</color>무시합니다.";
                break;
            case EnemyType.MovementSpeed:
                EnemyAttribute.text = $"<gradient=Enemy_MovementSpeed>활주</gradient>";
                AttributeText.text = "활주\n\n이동속도를 <color=green>6배</color>증가시킵니다.";
                break;
            case EnemyType.Boss:
                EnemyAttribute.text = $"<gradient=Enemy_Boss>불멸</gradient>";
                AttributeText.text = "불멸\n\n타워의 공격을 <color=green>50%</color>무시합니다." +
                                    "\n매<color=#68DEAB>5초</color>마다 최대 체력의 <color=green>2%</color> 회복합니다." +
                                    "\n일정 시간마다 타워를 공격합니다.";
                break;
        }
        EnemyArmor.text = $"{enemy.Armor}%";                                // 적 방어력 표시
        EnemyName.text = enemy.EnemyName;
    }
    public void ShutdownEnemyInfoUI()                                       // 적 정보 패널 끄기
    {
        EnemyInfoUI.SetActive(false);
        AttributeInfoPanel.SetActive(false);
    }

    public void ShowLabUI(int index)
    {
        if (index < 0 || index >= playerUIs.Count) return;
        TowerController.LoadUpgradeData();
        UpdateLabButtons();

        var ui = playerUIs[index];
        ui.TowerLabUI?.SetActive(true);
        BuildingStatusPanel.SetActive(false);
    }

    public void ShutdownLabUI()
    {
        int index = LocalPlayerIndex;
        if (index < 0 || index >= playerUIs.Count) return;
        playerUIs[index].TowerLabUI?.SetActive(false);
        BuildingStatusPanel.SetActive(false);
    }

    public void ShowLeaderBoardUI()                                         // 리더보드 패널 켜기
    {
        LeaderBoard_Button_UpButton.SetActive(true);
        LeaderBoard_Button_DownButton.SetActive(false);
        LeaderBoardUI.SetActive(true);
    }
    

    public void ShutdownLeaderBoardUI()                                     // 리더보드 패널 끄기
    {
        LeaderBoard_Button_UpButton.SetActive(false);
        LeaderBoard_Button_DownButton.SetActive(true);
        LeaderBoardUI.SetActive(false);
    }
    public void UpdatexButtonUI()
    {
        switch(GameManager.Instance.timeScale)
        {
            case (TimeScale.one):
                xButtonText.text = "배속 X1";
                break;
            case (TimeScale.two):
                xButtonText.text = "배속 X2";
                break;
        }
    }

    // 적 정보 패널에서 현재 적을 반환하는 함수
    public Enemy GetCurrentEnemy() => currentEnemy;

    // 타워 업그레이드 버튼 클릭 이벤트 핸들러
    public void OnCreateFlameTower() => OnCreateTowerButtonClicked(TowerType.Flame);
    public void OnCreateLaserTower() => OnCreateTowerButtonClicked(TowerType.Laser);
    public void OnCreateMachineTower() => OnCreateTowerButtonClicked(TowerType.Machine);
    public void OnCreateMultipleTower() => OnCreateTowerButtonClicked(TowerType.Multiple);
    public void OnCreateRocketTower() => OnCreateTowerButtonClicked(TowerType.Rocket);

    // 타워 연구 버튼 클릭 이벤트 핸들러
    public void OnUpgradeFlameTower()
    {
        if (!IsReady()) return;
        TryUpgradeLab(LocalPlayerIndex, TowerType.Flame);
    }

    public void OnUpgradeLaserTower()
    {
        if (!IsReady()) return;
        TryUpgradeLab(LocalPlayerIndex, TowerType.Laser);
    }

    public void OnUpgradeMachineTower()
    {
        if (!IsReady()) return;
        TryUpgradeLab(LocalPlayerIndex, TowerType.Machine);
    }

    public void OnUpgradeMultipleTower()
    {
        if (!IsReady()) return;
        TryUpgradeLab(LocalPlayerIndex, TowerType.Multiple);
    }

    public void OnUpgradeRocketTower()
    {
        if (!IsReady()) return;
        TryUpgradeLab(LocalPlayerIndex, TowerType.Rocket);
    }

    public void OnClickExitPanelOpen()
    {
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
        ExitPanel.SetActive(true);
    }

    public void OnClickExitPanelClose()
    {
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
        ExitPanel.SetActive(false);
    }

    public void OnClickExitGame()
    {
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
        Application.Quit();
    }

    public void OnClickGoToLobby()
    {
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
        UDPClient.Instance.ExitRoom_UDP();
    }
}