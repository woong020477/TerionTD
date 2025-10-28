using UnityEngine;
using UnityEngine.EventSystems;

public class PlayerController : MonoBehaviour, ILabSelectable
{
    // 플레이어 정보
    public int UserId { get; private set; }             // 플레이어 ID (서버에서 할당)
    public int PlayerIndex { get; private set; }        // 플레이어 인덱스 (0부터 시작)
    public string Username { get; private set; }        // 플레이어 이름
    public bool IsLocalPlayer { get; private set; }     // 로컬 플레이어 여부

    [Header("플레이어 골드")]
    private long gold;

    private BuildingSystem buildingSystem;
    private GameObject buildPrefab;

    private bool buildModeActive = false;
    private bool isWaitingToBuild = false;

    private GameObject pendingBuildPrefab;  // 대기 중인 빌드 프리팹 추후 리스트로 건설 대기열 확장 예정
    private Transform playerTransform; // 자신의 Transform 직접 지정하기위한 변수

    // 빌드 모드 활성화 여부
    public bool IsBuildModeActive => buildModeActive;

    public void Initialize(int userId, string username, int index,bool isLocal, BuildingSystem buildingSystem = null, GameObject buildPrefab = null)
    {
        this.UserId = userId;
        this.Username = username;
        this.PlayerIndex = index;
        this.IsLocalPlayer = isLocal; // 로컬 플레이어 여부 설정
        this.buildingSystem = buildingSystem;
        this.buildPrefab = buildPrefab;

        // UIManager에 자기 자신 등록
        UIManager.Instance.RegisterPlayerUI(index, this);

        Debug.Log($"{PlayerIndex + 1}P 생성됨: {Username}");
    }

    public void SetAsLocalPlayer(bool value)                    // 로컬 플레이어 여부 설정
    {
        IsLocalPlayer = value;
    }

    void Start()
    {
        /* 게임매니저 인풋 이벤트 구독 */
        GameManager.Input.NormAction += HandleBuildInput;

        if (playerTransform == null)
            playerTransform = this.transform; // 플레이어 Transform이 지정되지 않은 경우 자기 자신을 사용

        UIManager.Instance.Initialize(this);

        AddGold(1000); // 초기 골드 설정
    }

    private void Update()
    {
        HandleBuildCompletion();
        
        if(buildModeActive && (UIManager.Instance.UpgradeUI.activeSelf
            || UIManager.Instance.TowerLabUI.activeSelf)
            || Input.GetMouseButtonDown(1))    // 업그레이드 UI나 타워 연구소 UI가 열려있거나 우클릭 시 건설 모드 수동 비활성화
        {
            BuildToggleTrigger();
        }

        if (Input.GetKeyDown(KeyCode.B)) // B 키로 건설 모드 토글
        {
            UIManager.Instance.ToggleSelectTowerPanel(false); // 타워 선택 패널 닫기
            BuildToggle();
        }
        ShowShortkeyPanel();
    }

    // 빌드 모드를 토글하는 함수
    public void BuildToggle()
    {
        // 이동 모드가 그리드를 쓰고 있으면 거부
        if (BuildingSystem.Instance.IsOwnedBy(BuildingSystem.GridOwner.Move))
        {
            Debug.Log("[Build] 이동 중에는 건설을 시작할 수 없습니다.");
            return;
        }

        bool turnOn = !buildModeActive;

        if (turnOn)
        {
            // 그리드 예약 시도(건설)
            if (!BuildingSystem.Instance.BeginGrid(BuildingSystem.GridOwner.Build, this)) return;
        }
        else
        {
            // 예약 해제
            BuildingSystem.Instance.EndGrid(this);
        }

        buildModeActive = turnOn;
        UIManager.Instance.BuildModeUI.SetActive(buildModeActive);

        if (buildingSystem != null && buildingSystem.cursorIndicatorParent != null)
            buildingSystem.cursorIndicatorParent.SetActive(buildModeActive);

        Debug.Log($"[PlayerController] 건설 모드: {(buildModeActive ? "ON" : "OFF")}");
    }

    // 빌드 모드 실행중인 경우 끄는 함수
    void BuildToggleTrigger()
    {
        if (!buildModeActive) return;

        // 예약 해제
        BuildingSystem.Instance.EndGrid(this);

        buildModeActive = false;
        UIManager.Instance.BuildModeUI.SetActive(false);
        if (buildingSystem != null && buildingSystem.cursorIndicatorParent != null)
            buildingSystem.cursorIndicatorParent.SetActive(false);

        Debug.Log("[PlayerController] 건설 모드: OFF");
    }

    public void CancelBuildModeIfActive()
    {
        if (buildModeActive) BuildToggleTrigger();
    }

    // 선택되었을 때 업그레이드 UI를 보여주는 메소드
    public void OnSelected()
    {
        UIManager.Instance.ShowLabUI(PlayerIndex);
        if (buildModeActive) BuildToggle();
    }

    public void OnDeSelected()
    {
        UIManager.Instance.ShutdownLabUI();
    }

    // 버튼 입력에 따른 건설관련 기능을 처리하는 함수
    void HandleBuildInput()
    {
        if (!buildModeActive) return;

        // 좌클릭 시 건설 예약
        if (Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject())
        {
            if (buildingSystem != null && buildPrefab != null)
            {
                isWaitingToBuild = true;
                pendingBuildPrefab = buildPrefab;               // 대기 중인 빌드 프리팹 설정
                buildingSystem.LockCursor();                    // 커서 잠금
            }
        }
    }

    // Build를 실행하는 함수
    void HandleBuildCompletion()
    {
        if (!isWaitingToBuild || pendingBuildPrefab == null)
            return;

        if (UseGold(100))
        {
            GameObject baseObj = buildingSystem.TryBuild(pendingBuildPrefab);
            if (baseObj != null && baseObj.TryGetComponent<TowerBase>(out var towerBase))
            {
                int netId = buildingSystem.RegisterBase(towerBase);  // 새 Network ID 발급
                towerBase.Initialize(this, netId); // 소유자 설정

                // UDP 전송 (로컬 플레이어만)
                if (IsLocalPlayer && UDPClient.Instance != null)
                {
                    // 기존: TowerBuildMessage + towerType 전송 → 삭제
                    UDPClient.Instance.SendUDP(
                        JsonUtility.ToJson(new TowerBasePlaceMessage
                        {
                            ownerIndex = PlayerIndex,
                            baseNetId = netId,
                            position = baseObj.transform.position
                        }),
                        "TOWER_BASE_PLACE"
                    );
                }
            }

            buildingSystem.UnlockCursor();
            isWaitingToBuild = false;
            pendingBuildPrefab = null;
        }

        else {
            Debug.LogWarning("골드가 부족하여 건설할 수 없습니다!");
            buildingSystem.UnlockCursor(); // 커서 잠금 해제
            isWaitingToBuild = false;
            pendingBuildPrefab = null;
        }
    }

    // 골드 차감 함수
    public bool UseGold(long amount)
    {
        if (gold >= amount)
        {
            gold -= amount;
            Debug.Log($"골드 {amount} 소모됨. 남은 골드: {gold}");

            UIManager.Instance.UpdateGoldUI(PlayerIndex); // UI 업데이트
            return true;
        }
        else
        {
            Debug.LogWarning("골드가 부족합니다!");
            return false;
        }
    }

    // 골드 추가 함수
    public void AddGold(long amount)
    {
        if(this == null)
        {
            Debug.LogWarning("PlayerController가 null입니다. 골드 추가 실패.");
            return;
        }

        gold += amount;
        Debug.Log($"골드 {amount} 획득. 현재 골드: {gold}");
        UIManager.Instance.UpdateGoldUI(PlayerIndex); // UI 업데이트
    }

    // 현재 골드 값 가져오기
    public float GetGold()
    {
        return gold;
    }

    public void ShowShortkeyPanel()
    {
        if (Input.GetKey(KeyCode.J))
            UIManager.Instance.guidePanelflag = true;
        else
        {
            UIManager.Instance.guidePanelflag = false;
        }
        UIManager.Instance.GuidePanel.SetActive(UIManager.Instance.guidePanelflag);
    }

    private void OnDestroy()
    {
        UDPClient.Instance?.StopUDP();
    }
}