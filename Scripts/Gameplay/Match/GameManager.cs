using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 게임 시간 스케일을 정의하는 열거형
public enum TimeScale
{
    one,
    two
}

public enum MatchResult
{
    None,
    Victory,
    Defeat
}

/// <summary>매치 초기화와 호스트 권한 전환, 플레이어별 결과를 조정하는 게임 씬 진입점입니다.</summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("플레이어 관련")]
    public GameObject playerPrefab; // 플레이어 프리팹
    public Transform[] playerSpawnPoints = new Transform[4]; // 플레이어 스폰 포인트 배열
    [SerializeField]
    private BuildingSystem buildingSystem;
    [SerializeField]
    private GameObject defaultBuildPrefab;
    [SerializeField]
    private Button speedButton;
    [HideInInspector]
    public List<PlayerController> players = new List<PlayerController>(); // 플레이어 컨트롤러 리스트
    public PlayerController LocalPlayer { get; private set; }

    private int hostUserId;
    private bool IsHost() => hostUserId != 0 && hostUserId == AuthManager.loggedInUserId;
    public bool IsHostPlayer => initialized && IsHost();

    private double matchStartUnix;
    private readonly Dictionary<int, MatchResult> playerResults = new Dictionary<int, MatchResult>();
    private bool matchCompleted;
    public bool IsMatchFinished => matchCompleted;

    [Header("적 스포너")]
    [SerializeField]
    private List<EnemySpawner> spawners;
    [Header("적 경로")]
    [SerializeField]
    private List<Transform> enemyPath0;
    [SerializeField]
    private List<Transform> enemyPath1;
    [SerializeField]
    private List<Transform> enemyPath2;
    [SerializeField]
    private List<Transform> enemyPath3;
    [Header("리더보드 난이도 & 플레이어 이름 출력")]
    [SerializeField]
    private TMP_Text leaderboardDifficultyText;
    [SerializeField]
    private TMP_Text leaderboardPlayer1NameText;
    [SerializeField]
    private TMP_Text leaderboardPlayer2NameText;
    [SerializeField]
    private TMP_Text leaderboardPlayer3NameText;
    [SerializeField]
    private TMP_Text leaderboardPlayer4NameText;
    private bool initialized = false; // 초기화 여부
    private int PlayerCount = 4;
    private List<int> PlayerWaveCount; // 플레이어별 웨이브 카운트
    private TimeScale GameTimeScale; // 게임 시간 스케일
    public TimeScale timeScale
    {
        get
        {
            return GameTimeScale;
        }
    }

    private InputManager inputManager = new InputManager();
    public static InputManager Input
    {
        get
        {
            return Instance.inputManager;
        }
    }

    private bool objSpawned, buffApplied; // 이벤트 오브젝트 스폰 여부, 웨이브 버프 적용 여부
    [HideInInspector]
    public float difficultyEnemyBonusArmor; // 난이도에 따른 적 보너스 방어력
    [HideInInspector]
    public double difficultyEnemyBonusHealth; // 난이도에 따른 적 보너스 체력
    /// <summary>유지된 방 정보에서 참가자·호스트·난이도를 복원하고 게임 시스템을 준비합니다.</summary>
    public void Initialize(RoomInfo roomInfo)
    {
        if (initialized)
            return;
        inputManager = new InputManager();
        hostUserId = roomInfo.Players?.FirstOrDefault(player => player.IsHost)?.UserId ?? 0;
        leaderboardDifficultyText.text = roomInfo.Difficulty;
        switch (roomInfo.Difficulty)
        {
            case "Easy":
                difficultyEnemyBonusArmor = 0;
                difficultyEnemyBonusHealth = 0.5f;
                break;
            case "Normal":
                difficultyEnemyBonusArmor = 25;
                difficultyEnemyBonusHealth = 1;
                break;
            case "Hard":
                difficultyEnemyBonusArmor = 50;
                difficultyEnemyBonusHealth = 2;
                break;
        }

        InitializePlayers(roomInfo);
        initialized = true;
        if (speedButton != null)
            speedButton.interactable = IsHost();
        UIManager.Instance?.UpdatexButtonUI();
        Debug.Log("GameManager 초기화 완료");
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        PlayerWaveCount = Enumerable.Repeat(1, PlayerCount).ToList();
        GameTimeScale = TimeScale.one;
    }

    private void OnEnable()
    {
        AuthManager.OnHostChanged += ApplyHostChanged;
    }

    private void OnDisable()
    {
        AuthManager.OnHostChanged -= ApplyHostChanged;
    }

    private void Start()
    {
        if (initialized)
            return;
        var roomInfo = AuthManager.Instance.CurrentRoomInfo;
        if (roomInfo == null)
        {
            Debug.LogWarning("RoomInfo가 아직 준비되지 않았습니다. 초기화 보류 → 대기 코루틴 시작");
            StartCoroutine(WaitForRoomInfoAndInit());
            return;
        }

        EnsureUdpAndInit(roomInfo);
    }

    private void Update()
    {
        inputManager.OnUpdate();
        UpdateTimeScale();
    }

    // AuthManager가 준비되면 자동으로 호출되는 코루틴
    private IEnumerator WaitForRoomInfoAndInit()
    {
        // 필요시 타임아웃/취소 조건도 추가 가능
        while (AuthManager.Instance == null || AuthManager.Instance.CurrentRoomInfo == null)
            yield return null;
        var roomInfo = AuthManager.Instance.CurrentRoomInfo;
        EnsureUdpAndInit(roomInfo);
    }

    /// <summary>
    /// 로그인 세션과 동일한 서버 설정으로 UDP를 연결한 뒤, 방 정보 기준으로 게임 오브젝트를 초기화합니다.
    /// TCP와 UDP 주소가 서로 달라지는 배포 실수를 막기 위해 AuthManager 설정을 단일 원본으로 사용합니다.
    /// </summary>
    private void EnsureUdpAndInit(RoomInfo roomInfo)
    {
        if (AuthManager.Instance == null)
        {
            Debug.LogError("서버 설정을 가진 AuthManager가 없어 게임을 초기화할 수 없습니다.");
            return;
        }

        if (!initialized)
        {
            Initialize(roomInfo);
        }

        UDPClient.Instance?.StartUDP(AuthManager.Instance.ServerHost, roomInfo.RoomId, AuthManager.loggedInUserId, IsHost(), AuthManager.Instance.UdpServerPort);
    }

    // 호스트가 게임 시작할 때 한번 송신
    public void BroadcastGameStartIfHost()
    {
        if (!IsHost())
            return;
        double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        UDPClient.Instance?.SendUDP(JsonUtility.ToJson(new GameStartMessage { startUnix = now }), "GAME_START");
        ApplyGameStart(now);
    }

    // 호스트가 게임 시작 시 다른 플레이어에게 게임 시작 메시지를 전송
    public void ApplyGameStart(double unix)
    {
        matchStartUnix = unix;
        StopAllCoroutines();
    }

    public bool IsPlayerFinished(int ownerIndex)
    {
        return playerResults.ContainsKey(ownerIndex);
    }

    /// <summary>호스트가 개별 플레이어의 결과를 확정하고 나머지 참가자에게 알립니다.</summary>
    public void ReportPlayerResult(int ownerIndex, MatchResult result)
    {
        if (!IsHostPlayer || matchCompleted || result == MatchResult.None || IsPlayerFinished(ownerIndex))
            return;
        var message = new PlayerMatchResultMessage
        {
            ownerIndex = ownerIndex,
            result = result
        };
        StartCoroutine(BroadcastPlayerResult(message));
        ApplyPlayerResult(ownerIndex, result);
    }

    private IEnumerator BroadcastPlayerResult(PlayerMatchResultMessage message)
    {
        string json = JsonUtility.ToJson(message);
        for (int i = 0; i < 3; i++)
        {
            UDPClient.Instance?.SendUDP(json, "PLAYER_MATCH_RESULT");
            yield return new WaitForSecondsRealtime(0.1f);
        }
    }

    public void ApplyPlayerResult(int ownerIndex, MatchResult result)
    {
        if (result == MatchResult.None || playerResults.ContainsKey(ownerIndex))
            return;
        playerResults[ownerIndex] = result;
        bool victory = result == MatchResult.Victory;
        bool isLocalResult = LocalPlayer != null && LocalPlayer.PlayerIndex == ownerIndex;
        foreach (var spawner in spawners.Where(item => item != null && item.ownerIndex == ownerIndex))
            spawner.WaveManagerRef?.ApplyMatchFinished(victory, isLocalResult);
        if (!IsHostPlayer)
            return;
        int activeLaneCount = spawners.Count(item => item != null && item.IsActiveLane);
        if (activeLaneCount > 0 && playerResults.Count >= activeLaneCount)
        {
            var completion = new MatchCompleteMessage
            {
                results = playerResults.Select(item => new PlayerMatchResultMessage { ownerIndex = item.Key, result = item.Value }).ToArray()
            };
            StartCoroutine(BroadcastMatchComplete(completion));
            ApplyMatchComplete();
        }
    }

    private IEnumerator BroadcastMatchComplete(MatchCompleteMessage completion)
    {
        string json = JsonUtility.ToJson(completion);
        for (int i = 0; i < 3; i++)
        {
            UDPClient.Instance?.SendUDP(json, "MATCH_COMPLETE");
            yield return new WaitForSecondsRealtime(0.1f);
        }
    }

    public void ApplyMatchComplete()
    {
        if (matchCompleted)
            return;
        matchCompleted = true;
        if (IsHostPlayer && AuthManager.Instance?.CurrentRoomInfo != null)
            AuthManager.Instance.SendCloseMatchRoom(AuthManager.Instance.CurrentRoomInfo.RoomId);
        StartCoroutine(ReturnToLobbyAfterResult());
    }

    private IEnumerator ReturnToLobbyAfterResult()
    {
        yield return new WaitForSecondsRealtime(3f);
        UDPClient.Instance?.ExitRoom_UDP();
    }

    // 플레이어 초기화 메소드
    private void InitializePlayers(RoomInfo roomInfo)
    {
        players.Clear();
        LocalPlayer = null;
        // 실제 접속자만
        var joinPlayers = roomInfo.Players.Where(p => p.UserId != 0 && !(p.Username == "Open" || p.Username == "Close")).ToList();
        for (int i = 0; i < joinPlayers.Count; i++)
        {
            if (i >= playerSpawnPoints.Length)
            {
                Debug.LogWarning("스폰 포인트 개수보다 플레이어 수가 많습니다.");
                continue;
            }

            var playerData = joinPlayers[i];
            GameObject playerObj = Instantiate(playerPrefab, playerSpawnPoints[i].position, Quaternion.identity);
            var controller = playerObj.GetComponent<PlayerController>();
            // 로컬 여부 판별
            bool isLocal = playerData.UserId == AuthManager.loggedInUserId;
            // PlayerController 초기화
            controller.Initialize(playerData.UserId, playerData.Username, i, isLocal, buildingSystem: buildingSystem, buildPrefab: defaultBuildPrefab);
            players.Add(controller);
            // 로컬 플레이어 UI 초기화
            if (isLocal)
            {
                LocalPlayer = controller;
                UIManager.Instance.Initialize(controller);
            }

            // 리더보드 UI 텍스트 세팅
            string displayName = playerData.Username;
            switch (i)
            {
                case 0:
                    leaderboardPlayer1NameText.text = displayName;
                    break;
                case 1:
                    leaderboardPlayer2NameText.text = displayName;
                    break;
                case 2:
                    leaderboardPlayer3NameText.text = displayName;
                    break;
                case 3:
                    leaderboardPlayer4NameText.text = displayName;
                    break;
            }

            // 플레이어 인덱스에 따라 UI 업데이트
            if (spawners != null)
            {
                foreach (var s in spawners)
                {
                    if (s == null)
                        continue;
                    if (s.ownerIndex == controller.PlayerIndex)
                        s.InitializeSpawner(controller);
                }
            }
        }
    }

    /// <summary>
    /// 서버가 선출한 새 호스트를 적용하고, 새 호스트 클라이언트는 보유 중인 복제 상태를 권한 상태로 승격합니다.
    /// 나간 호스트의 라인은 개별 패배로 정리해 나머지 플레이어의 게임을 계속 진행합니다.
    /// </summary>
    public void ApplyHostChanged(int newHostUserId, int departedUserId)
    {
        if (newHostUserId <= 0)
            return;
        bool wasHost = IsHostPlayer;
        hostUserId = newHostUserId;
        if (speedButton != null)
            speedButton.interactable = IsHostPlayer;
        if (!initialized || wasHost || !IsHostPlayer)
            return;
        foreach (var spawner in spawners)
            spawner?.PromoteToAuthority();
        PlayerController departed = players.FirstOrDefault(player => player != null && player.UserId == departedUserId);
        if (departed != null)
            ReportPlayerResult(departed.PlayerIndex, MatchResult.Defeat);
    }

    // 플레이어 UserId로 Enemy 경로를 가져오는 메소드
    public List<Transform> GetEnemyPath(int playerIndex)
    {
        switch (playerIndex)
        {
            case 0:
                return enemyPath0;
            case 1:
                return enemyPath1;
            case 2:
                return enemyPath2;
            case 3:
                return enemyPath3;
            default:
                return null;
        }
    }

    // 게임 시간 스케일을 업데이트하는 메소드
    private void UpdateTimeScale()
    {
        switch (GameTimeScale)
        {
            case TimeScale.one:
                Time.timeScale = 1f;
                break;
            case TimeScale.two:
                Time.timeScale = 2f;
                break;
        }
    }

    // 플레이어가 타임스케일을 설정할 수 있는 메소드
    public void TrySetTimeScale(PlayerController caller)
    {
        if (caller == null || !caller.IsLocalPlayer || !IsHostPlayer)
        {
            Debug.LogWarning("게임 속도는 방장만 변경할 수 있습니다.");
            return;
        }

        SettingTimeScale();
    }

    // 타임스케일을 설정하는 메소드
    public void SettingTimeScale()
    {
        // 씬 Button 이벤트가 이 메서드를 직접 호출하므로 여기에서 권한을 반드시 검사한다.
        if (!IsHostPlayer)
        {
            Debug.LogWarning("게임 속도는 방장만 변경할 수 있습니다.");
            return;
        }

        if (GameTimeScale == TimeScale.one)
            GameTimeScale = TimeScale.two;
        else if (GameTimeScale == TimeScale.two)
            GameTimeScale = TimeScale.one;
        ApplyTimeScale(GameTimeScale == TimeScale.one ? 1f : 2f);
        StartCoroutine(BroadcastTimeScale(GameTimeScale == TimeScale.one ? 1f : 2f));
    }

    // 외부에서 타임스케일을 설정하는 메소드
    public void SetTimeScaleExternal(float s)
    {
        if (IsHostPlayer)
            return;
        ApplyTimeScale(s <= 1f ? 1f : 2f);
    }

    private void ApplyTimeScale(float scale)
    {
        Time.timeScale = scale;
        GameTimeScale = scale <= 1f ? TimeScale.one : TimeScale.two;
        UIManager.Instance?.UpdatexButtonUI();
    }

    private IEnumerator BroadcastTimeScale(float scale)
    {
        string json = JsonUtility.ToJson(new GameTimeScaleMessage { scale = scale });
        for (int i = 0; i < 3; i++)
        {
            UDPClient.Instance?.SendUDP(json, "GAME_TIMESCALE");
            yield return new WaitForSecondsRealtime(0.1f);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        Time.timeScale = 1f;
    }
}
