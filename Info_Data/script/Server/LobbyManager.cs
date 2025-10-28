using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance;

    [Header("UI Panel")]
    [SerializeField] private GameObject chatPanel;
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private GameObject roomPanel;
    [SerializeField] private GameObject roomFilterPanel;
    [SerializeField] private GameObject createRoomPanel;
    [SerializeField] private GameObject quitPanel;

    [Header("MenuPanel UI")]
    [SerializeField] private TMP_Text usernameText;

    [Header("RoomPanel UI")]
    [SerializeField] private GameObject createRoomButton;

    [Header("RoomPanel UI - RoomInfo")]
    public GameObject roomInfoPanel;              // RoomInfo UI Panel
    [SerializeField] private TMP_Text outputRoomNameText_RI;
    [SerializeField] private TMP_Text outputHostNameText_RI;
    [SerializeField] private TMP_Text outputDifficultyText_RI;

    private int lastClickedRoomId = -1;                             // 마지막으로 클릭한 RoomId 저장
    private float lastClickTime = 0f;                               // 마지막 클릭 시간
    private float doubleClickThreshold = 0.3f;                      // 더블클릭 인식 시간(초)

    [Header("RoomPanel UI - RoomJoin")]
    [SerializeField] private GameObject roomJoinPanel;
    [SerializeField] private TMP_Text outputRoomNameText_RJ;
    [SerializeField] private TMP_Text outputHostNameText_RJ;
    [SerializeField] private TMP_Text outputDifficultyText_RJ;

    [Header("RoomPanel UI - PlayerList")]
    [SerializeField] private GameObject playerSlotPrefab;           // 플레이어 슬롯 UI 프리팹
    [SerializeField] private Transform playerListContent_RJ;        // RoomJoin용
    [SerializeField] private Transform playerListContent_RI;        // RoomInfo용

    private int currentRoomId;
    private int hostUserId;

    [Header("RoomFilterPanel UI")]
    [SerializeField] private TMP_InputField hostUsernameInputField_RF;
    [SerializeField] private TMP_Dropdown difficultyDropdown_RF;
    [SerializeField] private TMP_InputField roomNameInputField_RF;

    private List<GameObject> currentRoomButtons = new List<GameObject>(); // 현재 생성된 RoomButton 캐싱

    [Header("CreateRoomPanel UI")]
    [SerializeField] private TMP_InputField roomNameInputField_CR;
    [SerializeField] private TMP_Dropdown difficultyDropdown_CR;
    [SerializeField] private Transform roomListContent;
    [SerializeField] private GameObject roomButtonPrefab;

    [Header("No Panel UI")]
    [SerializeField] private TMP_Text PingStatusText;
    [SerializeField] private TMP_Text lobbyStatusTextPrefab;
    [SerializeField] private Transform statusTextParent;

    private DOTweenAnimation roomJoinPanelTween;
    private DOTweenAnimation roomInfoPanelTween;

    private void Awake()
    {
        // 싱글톤 인스턴스 설정
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject); // 이미 인스턴스가 존재하면 중복 생성 방지
        }
    }

    private void Start()
    {
        // 칭호 UI 업데이트
        UpdateTitleUI();

        // Panel 속 DOTweenAnimation 컴포넌트 캐싱
        roomInfoPanelTween = roomInfoPanel.GetComponent<DOTweenAnimation>();
        roomJoinPanelTween = roomJoinPanel.GetComponent<DOTweenAnimation>();

        // 난이도 DropDown에 All 옵션이 없으면 추가
        if (difficultyDropdown_RF != null)
        {
            bool hasAll = difficultyDropdown_RF.options.Exists(o => o.text == "All");
            if (!hasAll)
            {
                difficultyDropdown_RF.options.Insert(0, new TMP_Dropdown.OptionData("All"));
                difficultyDropdown_RF.value = 0; // 기본값을 All로 설정
            }
        }

        // 방 리스트 요청
        AuthManager.Instance?.SendRequestRoomList();
    }

    private void Update()
    {
        // DOTween 업데이트
        UpdateDotweens();

        if (PingStatusText != null)
        {
            // 핑 상태 업데이트
            PingStatusText.text = $"{Mathf.FloorToInt(AuthManager.currentPing)}ms";
        }

        if (PingStatusText != null)
        {
            PingStatusText.text = $"{Mathf.FloorToInt(AuthManager.currentPing)}ms";
        }

        // Panel 외부 클릭 시 닫기
        if (roomFilterPanel.activeSelf && Input.GetMouseButtonDown(0))
        {
            // 현재 클릭된 UI 오브젝트 가져오기
            GameObject clickedObject = EventSystem.current.currentSelectedGameObject;

            // 클릭된 오브젝트가 없거나, panel과 무관하면 닫기
            if (clickedObject == null || !clickedObject.transform.IsChildOf(roomFilterPanel.transform))
            {
                roomFilterPanel.SetActive(false);
            }
        }
    }

    private void OnEnable()
    {
        AuthManager.OnRoomListReceived += UpdateRoomListUI;
        AuthManager.OnRoomUpdateReceived += UpdateRoomInfoUI;
        AuthManager.OnRoomUpdateReceived += HandleRoomUpdate;
        AuthManager.OnRoomActionResult += HandleRoomActionResult;
        AuthManager.OnRoomInfoReceived += UpdateRoomInfoPanel;
    }

    private void OnDisable()
    {
        AuthManager.OnRoomListReceived -= UpdateRoomListUI;
        AuthManager.OnRoomUpdateReceived -= UpdateRoomInfoUI;
        AuthManager.OnRoomUpdateReceived -= HandleRoomUpdate;
        AuthManager.OnRoomActionResult -= HandleRoomActionResult;
        AuthManager.OnRoomInfoReceived -= UpdateRoomInfoPanel;
    }

    public void UpdateDotweens()
    {
        // roomJoinPanel, roomInfoPanel 활성화 상태에 따라 트윈 재생 / 되감기

        if (roomJoinPanel.activeSelf)
        {
            roomJoinPanelTween.DOPlay();
        }
        else if (!roomJoinPanel.activeSelf)
        {
            roomJoinPanelTween.DORewind();
        }

        if (roomInfoPanel.activeSelf)
        {
            roomInfoPanelTween.DOPlay();
        }
        else if (!roomInfoPanel.activeSelf)
        {
            roomInfoPanelTween.DORewind();
        }
    }

    public void OnClickOptionButton()
    {
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
        // 옵션 버튼 클릭 시 OptionManager의 OnClickOpen 호출
        OptionManager.Instance.OnClickOpen();
    }

    public void UpdateTitleUI()
    {
        if (AuthManager.loggedInTitleId > 0)
            usernameText.text = $"<gradient={AuthManager.loggedInColorGradient}>{AuthManager.loggedInUsername}</gradient>";
        else
            usernameText.text = AuthManager.loggedInUsername;
    }

    private void HandleRoomActionResult(string command, string message)
    {
        // 방 액션 결과 처리
        if (command == "room-closed")
        {
            HandleRoomClosed(currentRoomId);
        }

        if (command == "game-start")
        {
            // 1) 내 UI 정리
            createRoomButton.SetActive(true);
            roomJoinPanel.SetActive(false);
            // 2) 해당 방 버튼 제거(로컬 즉시 반영)
            RemoveRoomButtonById(currentRoomId);
            // 3) 최신 리스트 재요청(로비에 남아있는 다른 유저 화면도 맞추기)
            AuthManager.Instance?.SendRequestRoomList();
            // 4) 선택 해제
            currentRoomId = 0;
        }

        // UI 텍스트로 결과 메시지 표시
        if (lobbyStatusTextPrefab != null)
        {
            lobbyStatusTextPrefab.text = message;
            Debug.Log($"[방 액션 결과] {command} → {message}");
        }
    }

    // 게임 시작 브로드캐스트 수신 시 호출
    public void OnRoomGameStartedBroadcast(int roomId)
    {
        RemoveRoomButtonById(roomId);
        AuthManager.Instance?.SendRequestRoomList();
    }

    private void HandleRoomUpdate(string json)
    {
        try
        {
            var data = JsonUtility.FromJson<RoomUpdateData>(json);
            if (data != null && data.Players != null)
            {
                UpdateRoomPlayersUI(data);
            }
            else
            {
                Debug.LogWarning("room-update 패킷 파싱 실패 또는 플레이어 리스트가 비어 있음");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("room-update 파싱 중 오류 발생: " + ex.Message);
        }
    }

    public void SetCurrentRoomId(int roomId)
    {
        currentRoomId = roomId;
        Debug.Log($"현재 방 ID가 {currentRoomId}로 설정되었습니다.");
    }

    public int GetCurrentRoomId()
    {
        return currentRoomId;
    }
    public void RoomPanel_OnRefreshRoomListClick()
    {
        Debug.Log("방 리스트 갱신 요청");
        AuthManager.Instance.SendRequestRoomList();
    }
    // ----------------- Room Panel - RoomInfo -----------------

    private void UpdateRoomInfoPanel(string json)
    {
        var data = JsonUtility.FromJson<RoomUpdateData>(json);
        if (data == null) return;

        // 패널 활성화
        roomInfoPanel.SetActive(true);

        outputRoomNameText_RI.text = data.RoomName;
        outputHostNameText_RI.text = data.Host;
        outputDifficultyText_RI.text = data.Difficulty;

        // 슬롯 UI 갱신 (버튼 없는 상태로)
        foreach (Transform child in playerListContent_RI)
            Destroy(child.gameObject);

        foreach (var p in data.Players)
        {
            var slot = Instantiate(playerSlotPrefab, playerListContent_RI);

            // 색상 처리
            var img = slot.GetComponent<Image>();
            if (img != null)
            {
                Color slotColor = Color.white;
                switch (p.PlayerSlot)
                {
                    case 1: ColorUtility.TryParseHtmlString("#CA3038", out slotColor); break;
                    case 2: ColorUtility.TryParseHtmlString("#3D79A7", out slotColor); break;
                    case 3: ColorUtility.TryParseHtmlString("#508E34", out slotColor); break;
                    case 4: ColorUtility.TryParseHtmlString("#8544AD", out slotColor); break;
                }
                if (p.IsClosed) ColorUtility.TryParseHtmlString("#7B7B7B", out slotColor);
                img.color = slotColor;
            }

            // 텍스트
            TMP_Text[] texts = slot.GetComponentsInChildren<TMP_Text>(true);
            foreach (var t in texts)
            {
                if (t.name == "PlayerNameText")
                    t.text = p.IsClosed
                        ? "Close"
                        : (p.UserId == 0
                            ? "Open"
                            : $"<gradient={p.ColorGradient}>{p.Username}</gradient>");
                else
                    t.text = p.IsClosed ? "Close" : (p.UserId == 0 ? "Open" : p.Username);
            }

            // 호스트 아이콘
            Transform hostIcon = slot.transform.Find("HostIcon");
            if (hostIcon != null)
                hostIcon.gameObject.SetActive(p.IsHost);

            // 모든 버튼 비활성화 (RoomInfoPanel은 정보만 표시)
            Button[] buttons = slot.GetComponentsInChildren<Button>(true);
            foreach (var btn in buttons) btn.gameObject.SetActive(false);
        }
    }

    // ----------------- Room Panel - RoomJoin -----------------
    private void UpdateRoomInfoUI(string json)
    {
        var data = JsonUtility.FromJson<RoomUpdateData>(json);
        currentRoomId = data.RoomId;
        hostUserId = data.Players.FirstOrDefault(p => p.IsHost)?.UserId ?? 0;   // 호스트 UserId 저장

        roomJoinPanel.SetActive(true);
        createRoomButton.SetActive(false);

        outputRoomNameText_RJ.text = data.RoomName;
        outputHostNameText_RJ.text = data.Host;
        outputDifficultyText_RJ.text = data.Difficulty;

        UpdateRoomPlayersUI(data);
    }

    // 서버로부터 room-update 수신 시 호출
    public void UpdateRoomPlayersUI(RoomUpdateData data)
    {
        if (playerSlotPrefab == null)
        {
            Debug.LogError("PlayerSlotPrefab이 연결되지 않았습니다.");
            return;
        }
        if (playerListContent_RJ == null)
        {
            Debug.LogError("PlayerListContent가 연결되지 않았습니다.");
            return;
        }

        // 기존 슬롯 제거
        foreach (Transform child in playerListContent_RJ)
            Destroy(child.gameObject);

        // 플레이어 데이터 Dictionary로 변환
        Dictionary<int, PlayerData> playerDict = new Dictionary<int, PlayerData>();
        if (data.Players != null)
        {
            foreach (var p in data.Players)
            {
                if (!playerDict.ContainsKey(p.PlayerSlot))
                    playerDict.Add(p.PlayerSlot, p);
            }
        }

        // 1~4 슬롯 강제 생성
        for (int i = 1; i <= 4; i++)
        {
            PlayerData player;
            if (!playerDict.TryGetValue(i, out player))
            {
                // 비어 있는 슬롯 데이터 생성
                player = new PlayerData
                {
                    PlayerSlot = i,
                    UserId = 0,
                    Username = "Open",
                    IsHost = false,
                    IsClosed = false
                };
            }

            // 슬롯 프리팹 생성
            CreatePlayerSlot(player);
        }

        Debug.Log("플레이어 슬롯 UI가 갱신되었습니다.");
    }


    // 플레이어 슬롯 생성
    private void CreatePlayerSlot(PlayerData player)
    {
        // 슬롯 프리팹 생성
        var slotObj = Instantiate(playerSlotPrefab, playerListContent_RJ);

        // ---------- 슬롯 색상 처리 ----------
        // PlayerSlot 오브젝트의 Image 컴포넌트 찾기
        Image slotImage = slotObj.GetComponent<Image>();

        if (slotImage != null)
        {
            // 슬롯별 기본 색상 지정
            Color slotColor = Color.white;
            switch (player.PlayerSlot)
            {
                case 1: ColorUtility.TryParseHtmlString("#CA3038", out slotColor); break;
                case 2: ColorUtility.TryParseHtmlString("#3D79A7", out slotColor); break;
                case 3: ColorUtility.TryParseHtmlString("#508E34", out slotColor); break;
                case 4: ColorUtility.TryParseHtmlString("#8544AD", out slotColor); break;
            }

            // Close 상태면 회색으로 변경
            if (player.IsClosed)
                ColorUtility.TryParseHtmlString("#7B7B7B", out slotColor);

            slotImage.color = slotColor;
        }

        // ---------- 텍스트 갱신 ----------
        TMP_Text[] texts = slotObj.GetComponentsInChildren<TMP_Text>(true);
        foreach (var t in texts)
        {
            switch (t.name)
            {
                case "PlayerNameText":
                    t.text = player.IsClosed
                        ? "Close"
                        : (player.UserId == 0
                            ? "Open"
                            : $"<gradient={player.ColorGradient}>{player.Username}</gradient>");
                    break;
            }
        }

        // ---------- 호스트 아이콘 갱신 ----------
        Transform hostIcon = slotObj.transform.Find("HostIcon");
        if (hostIcon != null)
            hostIcon.gameObject.SetActive(player.IsHost);

        // ---------- 버튼 자동 연결 ----------
        Button[] buttons = slotObj.GetComponentsInChildren<Button>(true);
        int slotNumber = player.PlayerSlot;
        bool isHostUser = AuthManager.loggedInUserId == hostUserId;

        foreach (var btn in buttons)
        {
            switch (btn.name)
            {
                case "UpButton":
                    btn.onClick.AddListener(() =>
                        RoomJoin_OnMoveSlotClick(slotNumber, slotNumber - 1 < 1 ? 4 : slotNumber - 1));
                    btn.gameObject.SetActive(isHostUser && player.UserId != 0);
                    break;

                case "DownButton":
                    btn.onClick.AddListener(() =>
                        RoomJoin_OnMoveSlotClick(slotNumber, slotNumber + 1 > 4 ? 1 : slotNumber + 1));
                    btn.gameObject.SetActive(isHostUser && player.UserId != 0);
                    break;

                case "HostButton":
                    btn.onClick.AddListener(() => RoomJoin_OnChangeHostClick(player.UserId));
                    btn.gameObject.SetActive(isHostUser && !player.IsHost && player.UserId != 0);
                    break;

                case "OpenButton":
                    btn.onClick.AddListener(() => RoomJoin_OnOpenSlotClick(slotNumber));
                    btn.gameObject.SetActive(isHostUser && player.UserId == 0 && player.IsClosed);
                    break;

                case "CloseButton":
                    btn.onClick.AddListener(() => RoomJoin_OnCloseSlotClick(slotNumber));
                    btn.gameObject.SetActive(isHostUser && player.UserId == 0 && !player.IsClosed);
                    break;

                case "KickButton":
                    btn.onClick.AddListener(() => RoomJoin_OnKickSlotClick(player.UserId));
                    btn.gameObject.SetActive(isHostUser && !player.IsHost && player.UserId != 0);
                    break;
            }
        }
    }

    // 버튼 이벤트
    public void RoomJoin_OnMoveSlotClick(int fromSlot, int toSlot)
    {
        // 슬롯을 이동할 때 요청
        AuthManager.Instance.SendMoveSlot(currentRoomId, fromSlot, toSlot);
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
    }

    public void RoomJoin_OnChangeHostClick(int userId)
    {
        // 현재 슬롯이 호스트가 아닌 경우에만 요청
        AuthManager.Instance.SendChangeHost(currentRoomId, userId);
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
    }

    public void RoomJoin_OnOpenSlotClick(int slot)
    {
        // 현재 슬롯이 닫혀있지 않은 경우에만 요청
        AuthManager.Instance.SendOpenSlot(currentRoomId, slot);
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
    }

    public void RoomJoin_OnCloseSlotClick(int slot)
    {
        //슬롯이 열려있는 경우에만 요청
        AuthManager.Instance.SendCloseSlot(currentRoomId, slot);
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
    }

    public void RoomJoin_OnKickSlotClick(int targetUserId)
    {
        if (currentRoomId == 0)
        {
            Debug.LogWarning("방 정보가 없습니다. 강퇴 요청을 보낼 수 없습니다.");
            return;
        }

        // 자기 자신은 강퇴할 수 없음
        if (targetUserId == AuthManager.loggedInUserId)
        {
            Debug.LogWarning("자기 자신은 강퇴할 수 없습니다.");
            return;
        }
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
        // AuthManager를 통해 서버로 강퇴 요청
        AuthManager.Instance.SendKickSlot(currentRoomId, targetUserId);
    }


    public void RoomJoin_OnExitRoomButtonClick()
    {
        bool isHost = (AuthManager.loggedInUserId == hostUserId);

        if (isHost)
        {
            createRoomButton.SetActive(true);
            roomJoinPanel.SetActive(false);
            Debug.Log("호스트가 방을 종료합니다.");
        }
        else
        {
            createRoomButton.SetActive(true);
            roomJoinPanel.SetActive(false);
            Debug.Log("플레이어가 방에서 나갑니다.");
        }
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
        AuthManager.Instance.SendExitRoom(currentRoomId);
    }

    public void HandleRoomClosed(int roomId)
    {
        if (currentRoomId == roomId)
        {
            bool isHost = (AuthManager.loggedInUserId == hostUserId);

            if (isHost)
            {
                createRoomButton.SetActive(true);
                roomJoinPanel.SetActive(false);
                PrintStatusText("방을 종료했습니다.", Color.green);
                
            }
            else
            {
                createRoomButton.SetActive(true);
                roomJoinPanel.SetActive(false);
                PrintStatusText("호스트가 방을 종료하여 방이 닫혔습니다.", Color.red);
            }
        }
    }

    // 게임 시작 버튼 클릭 시 호출
    public void OnClickStartGame()
    {
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
        int roomId = currentRoomId;
        string json = $"{{\"Command\":\"game-start\",\"RoomId\":{roomId}}}";
        AuthManager.Instance.SafeSend(AuthManager.sharedStream, Encoding.UTF8.GetBytes(json));
    }

    // ----------------- Room Filter Panel -----------------
    public void RoomFilter_OnButtonClick()
    {
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
        // 방 필터 버튼 클릭 시 RoomFilterPanel 활성화
        roomFilterPanel.SetActive(true);
    }

    public void RoomFilter_OnBackButtonClick()
    {
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
        // 뒤로가기 버튼 클릭 시 RoomFilterPanel 비활성화
        roomFilterPanel.SetActive(false);
    }

    // 방 필터링 기능(인풋 필드 값 변경 시 호출)
    public void RoomFilter_OnRoomFilterValueChanged()
    {
        string filterName = roomNameInputField_RF.text.Trim().ToLower();

        foreach (var btn in currentRoomButtons)
        {
            if (btn == null) continue;

            var data = btn.GetComponent<RoomButtonData>();
            if (data == null) continue;

            // 이름 필터링
            // 필터가 비어있거나, 버튼 이름에 필터 문자열이 포함되어 있는지 확인
            bool visible = string.IsNullOrEmpty(filterName) || data.RoomName.ToLower().Contains(filterName);
            btn.SetActive(visible);
        }
    }

    // 방 필터링 기능(필터 적용 버튼 클릭 시 호출)
    public void RoomFilter_OnRoomFilterApplyClick()
    {
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
        string filterHost = hostUsernameInputField_RF.text.Trim().ToLower();
        string filterDiff = difficultyDropdown_RF.options[difficultyDropdown_RF.value].text.ToLower();

        foreach (var btn in currentRoomButtons)
        {
            if (btn == null) continue;

            var data = btn.GetComponent<RoomButtonData>();
            if (data == null) continue;

            bool visible = true;

            // Host 필터 (부분 일치 허용)
            if (!string.IsNullOrEmpty(filterHost) && !data.HostName.ToLower().Contains(filterHost))
                visible = false;

            // Difficulty 필터
            if (!string.IsNullOrEmpty(filterDiff) && filterDiff != "all" && data.Difficulty.ToLower() != filterDiff)
                visible = false;

            btn.SetActive(visible);
        }
    }

    // ----------------- Create Room Panel -----------------
    public void CreateRoom_OnButtonClick()
    {
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
        // 방 생성 버튼 클릭 시 CreateRoomPanel 활성화
        createRoomPanel.SetActive(true);
    }
    public void CreateRoom_OnBackButtonClick()
    {
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
        // 뒤로가기 버튼 클릭 시 RoomCreatePanel 비활성화
        createRoomPanel.SetActive(false);
    }

    public void CreateRoom_OnCreateRoomButtonClick()
    {
        string roomName = roomNameInputField_CR.text;
        string hostName = AuthManager.loggedInUsername;
        string difficulty = difficultyDropdown_CR.options[difficultyDropdown_CR.value].text;

        AuthManager.Instance.CreateRoom(roomName, hostName, difficulty);
        createRoomPanel.SetActive(false);
        
    }

    // ----------------- Room Panel -----------------
    private void UpdateRoomListUI(string json)
    {
        try
        {
            var roomData = JsonUtility.FromJson<RoomListWrapper>(json);
            if (roomData == null || roomData.Rooms == null)
            {
                Debug.LogWarning("방 리스트가 비어있거나 파싱 과정에 오류가 발생했습니다.");
                return;
            }

            // 기존 리스트 초기화
            foreach (Transform child in roomListContent)
                Destroy(child.gameObject);
            currentRoomButtons.Clear();

            foreach (var room in roomData.Rooms)
            {
                if (room == null) continue;

                var btnObj = Instantiate(roomButtonPrefab, roomListContent);
                btnObj.transform.Find("RoomNameText").GetComponent<TMP_Text>().text = room.RoomName ?? "NoName";
                btnObj.transform.Find("RoomDifficultyText").GetComponent<TMP_Text>().text = room.Difficulty ?? "Unknown";
                btnObj.transform.Find("RoomStatusText").GetComponent<TMP_Text>().text =
                    $"Join Player [{room.CurrentPlayers}/{room.MaxPlayers}]\nHost : <gradient={room.ColorGradient}>{room.Host}</gradient>";

                // 태그 데이터 보관 (필터링용)
                var dataComp = btnObj.AddComponent<RoomButtonData>();
                dataComp.RoomId = room.RoomId;
                dataComp.RoomName = room.RoomName;
                dataComp.HostName = room.Host;
                dataComp.Difficulty = room.Difficulty;
                
                currentRoomButtons.Add(btnObj);

                btnObj.GetComponent<Button>().onClick.AddListener(() =>
                {
                    HandleRoomButtonClick(dataComp.RoomId, dataComp.RoomName, dataComp.HostName, dataComp.Difficulty);
                });
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("방 리스트 업데이트 실패: " + ex.Message + "\nJSON: " + json);
        }
    }

    private void HandleRoomButtonClick(int roomId, string roomName, string hostName, string difficulty)
    {
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
        // 이미 참가한 방은 RoomInfoPanel 활성화 불가
        if (roomId == currentRoomId && currentRoomId != 0)
        {
            PrintStatusText("이미 참여중인 방 입니다.", Color.red);
            return;
        }

        float timeSinceLastClick = Time.time - lastClickTime;

        if (lastClickedRoomId == roomId && timeSinceLastClick <= doubleClickThreshold)
        {
            // 현재 다른 방에 이미 참가 중이면 다른 방으로 이동 불가
            if (currentRoomId != 0 && currentRoomId != roomId)
            {
                PrintStatusText("이미 다른 방에 참여중입니다.\n 다른 방으로는 입장할 수 없습니다.", Color.red);
                return;
            }

            // ---------- 두 번 연속 클릭 시 RoomJoin ----------
            Debug.Log($"[더블클릭] RoomId {roomId} → Join 요청");
            AuthManager.Instance.JoinRoom(roomId);
            roomInfoPanel.SetActive(false);  // RoomInfo 비활성화
        }
        else
        {
            // ---------- 첫 번째 클릭 시 RoomInfo 활성화 ----------
            lastClickedRoomId = roomId;
            lastClickTime = Time.time;

            roomInfoPanel.SetActive(true);
            AuthManager.Instance.RequestRoomInfo(roomId);

            Debug.Log($"[단일클릭] RoomId {roomId} → RoomInfoPanel 활성화");
        }
    }

    // 특정 RoomId에 해당하는 RoomButton 제거
    private void RemoveRoomButtonById(int roomId)
    {
        for (int i = currentRoomButtons.Count - 1; i >= 0; i--)
        {
            var go = currentRoomButtons[i];
            if (!go) { currentRoomButtons.RemoveAt(i); continue; }

            var data = go.GetComponent<RoomButtonData>();
            if (data != null && data.RoomId == roomId)
            {
                Destroy(go);
                currentRoomButtons.RemoveAt(i);
                break;
            }
        }
    }

    // ----------------- Quit Panel -----------------
    public void QuitOption_OnButtonClick()
    {
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
        // 종료 버튼 클릭 시 QuitPanel 활성화
        quitPanel.SetActive(true);
    }
    public void QuitOption_OnLogoutButtonClick()
    {
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
        // 로그아웃 버튼 클릭 시 AuthManager의 로그아웃 메서드 호출
        AuthManager.Instance.Logout();
    }

    public void QuitOption_OnBackButtonClick()
    {
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
        // 뒤로가기 버튼 클릭 시 QuitPanel 비활성화
        quitPanel.SetActive(false);
    }

    public void QuitOption_OnQuitGameButtonClick()
    {
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
        // 게임종료 버튼 클릭 시 AuthManager의 종료 메서드 호출
        AuthManager.Instance.QuitGame();
        Application.Quit(); // 빌드된 게임에서 종료
    }

    // ---------------- 상태 텍스트 출력 기능 ----------------

    /// <summary>
    /// 상태 텍스트를 새로 생성하고 5초 뒤 서서히 사라지게 만드는 함수
    /// </summary>
    /// <param name="text">출력할 문자열</param>
    /// <param name="textColor">텍스트 색상</param>
    public void PrintStatusText(string text, Color textColor)
    {
        // TMP_Text 오브젝트를 복제 생성
        TMP_Text newText = Instantiate(lobbyStatusTextPrefab, statusTextParent);

        // 내용과 색상 설정
        newText.text = text;
        newText.color = textColor;

        // 오브젝트 활성화
        newText.gameObject.SetActive(true);

        // 사라지기 코루틴 실행
        StartCoroutine(FadeAndDestroyText(newText));
    }

    /// <summary>
    /// 5초 후 서서히 사라지고 파괴되는 코루틴
    /// </summary>
    private IEnumerator FadeAndDestroyText(TMP_Text textObj)
    {
        yield return new WaitForSeconds(0.5f);

        if (textObj == null)
            yield break;

        while (textObj != null && textObj.color.a > 0)
        {
            var color = textObj.color;
            color.a -= Time.deltaTime;
            textObj.color = color;
            yield return null;
        }

        if (textObj != null)
            Destroy(textObj.gameObject);
    }
}

[System.Serializable]
public class RoomUpdateData
{
    public string RoomName;
    public int RoomId;
    public string Host;
    public string Difficulty;
    public string ColorGradient;
    public PlayerData[] Players;
}

[System.Serializable]
public class PlayerData
{
    public int PlayerSlot;
    public int UserId;
    public string Username;
    public bool IsHost;
    public bool IsClosed;
    public int TitleId;
    public string TitleName;
    public string ColorGradient;
}

[System.Serializable]
public class RoomListWrapper
{
    public List<RoomData> Rooms;
}


[System.Serializable]
public class RoomData
{
    public int RoomId;
    public string RoomName;
    public string Host;
    public string Difficulty;
    public int CurrentPlayers;
    public int MaxPlayers;
    public string ColorGradient;
}