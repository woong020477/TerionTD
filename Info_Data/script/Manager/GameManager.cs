using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

// 게임 시간 스케일을 정의하는 열거형
public enum TimeScale
{
    one,
    two
}

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("플레이어 관련")]
    public GameObject playerPrefab;                                                                     // 플레이어 프리팹
    public Transform[] playerSpawnPoints = new Transform[4];                                            // 플레이어 스폰 포인트 배열
    
    [SerializeField] private BuildingSystem buildingSystem;
    [SerializeField] private GameObject defaultBuildPrefab;

    [HideInInspector] public List<PlayerController> players = new List<PlayerController>();             // 플레이어 컨트롤러 리스트

    private bool IsHost() => players.Count > 0 && players[0].UserId == AuthManager.loggedInUserId;
    private double matchStartUnix;

    [Header("적 스포너")]
    [SerializeField] private List<EnemySpawner> spawners;

    [Header("적 경로")]
    [SerializeField] private List<Transform> enemyPath0;
    [SerializeField] private List<Transform> enemyPath1;
    [SerializeField] private List<Transform> enemyPath2;
    [SerializeField] private List<Transform> enemyPath3;

    [Header("리더보드 난이도 & 플레이어 이름 출력")]
    [SerializeField] private TMP_Text leaderboardDifficultyText;
    [SerializeField] private TMP_Text leaderboardPlayer1NameText;
    [SerializeField] private TMP_Text leaderboardPlayer2NameText;
    [SerializeField] private TMP_Text leaderboardPlayer3NameText;
    [SerializeField] private TMP_Text leaderboardPlayer4NameText;

    private bool initialized = false;                                               // 초기화 여부
    private int PlayerCount = 4;
    private List<int> PlayerWaveCount;                                              // 플레이어별 웨이브 카운트

    private TimeScale GameTimeScale;                                                // 게임 시간 스케일
    public TimeScale timeScale { get { return GameTimeScale; } }

    private InputManager inputManager = new InputManager();
    public static InputManager Input { get { return Instance.inputManager; } }

    private bool objSpawned, buffApplied;                                           // 이벤트 오브젝트 스폰 여부, 웨이브 버프 적용 여부

    [HideInInspector] public float difficultyEnemyBonusArmor;                      // 난이도에 따른 적 보너스 방어력
    [HideInInspector] public double difficultyEnemyBonusHealth;                     // 난이도에 따른 적 보너스 체력

    // 초기화 메소드
    public void Initialize(RoomInfo roomInfo)
    {
        if (initialized) return;

        inputManager = new InputManager();

        leaderboardDifficultyText.text = roomInfo.Difficulty;

        switch(roomInfo.Difficulty)
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
        DontDestroyOnLoad(gameObject);

        PlayerWaveCount = Enumerable.Repeat(1, PlayerCount).ToList();
        GameTimeScale = TimeScale.one;
    }

    private void Start()
    {
        if (initialized) return;

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

    // UDP 클라이언트 시작 및 초기화
    private void EnsureUdpAndInit(RoomInfo roomInfo)
    {
        // --- UDP 시작(중복 방지 내장) ---
        string serverIp = "125.185.223.56";
        UDPClient.Instance?.StartUDP(serverIp);

        if (!initialized)
        {
            Initialize(roomInfo);
        }
    }

    // 호스트가 게임 시작할 때 한번 송신
    public void BroadcastGameStartIfHost()
    {
        if (!IsHost()) return;
        double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        UDPClient.Instance?.SendUDP(JsonUtility.ToJson(new GameStartMessage
        {
            startUnix = now
        }), "GAME_START");
        ApplyGameStart(now); // 로컬도 즉시 세팅
    }

    // 호스트가 게임 시작 시 다른 플레이어에게 게임 시작 메시지를 전송
    public void ApplyGameStart(double unix)
    {
        matchStartUnix = unix;
        StopAllCoroutines();
    }

    // 플레이어 초기화 메소드
    private void InitializePlayers(RoomInfo roomInfo)
    {
        players.Clear();

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
            controller.Initialize(
                playerData.UserId,
                playerData.Username,
                i,
                isLocal, // 로컬 여부 전달
                buildingSystem: buildingSystem,
                buildPrefab: defaultBuildPrefab
            );

            players.Add(controller);

            // 로컬 플레이어 UI 초기화
            if (isLocal)
            {
                UIManager.Instance.Initialize(controller); // 로컬 UI 바인딩
            }

            // 리더보드 UI 텍스트 세팅
            string displayName = playerData.Username;

            switch (i)
            {
                case 0: leaderboardPlayer1NameText.text = displayName; break;
                case 1: leaderboardPlayer2NameText.text = displayName; break;
                case 2: leaderboardPlayer3NameText.text = displayName; break;
                case 3: leaderboardPlayer4NameText.text = displayName; break;
            }

            // 플레이어 인덱스에 따라 UI 업데이트
            if (IsHost() && spawners != null)
            {
                foreach (var s in spawners)
                {
                    if (s == null) continue;
                    if (s.ownerIndex == controller.PlayerIndex) // ownerIndex와 플레이어 인덱스가 같을 때만
                        s.InitializeSpawner(controller);
                }
            }
        }
    }

    // 플레이어 UserId로 Enemy 경로를 가져오는 메소드
    public List<Transform> GetEnemyPath(int playerIndex)
    {
        switch (playerIndex)
        {
            case 0: return enemyPath0;
            case 1: return enemyPath1;
            case 2: return enemyPath2;
            case 3: return enemyPath3;
            default: return null;
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
        if (caller.PlayerIndex == 0 && caller.UserId == AuthManager.loggedInUserId)
        {
            SettingTimeScale();
            UDPClient.Instance?.SendUDP(JsonUtility.ToJson(new GameTimeScaleMessage
            {
                scale = (GameTimeScale == TimeScale.one) ? 1f : 2f
            }), "GAME_TIMESCALE");
        }
        else Debug.LogWarning("타임스케일은 1P만 설정할 수 있습니다.");
    }

    // 타임스케일을 설정하는 메소드
    public void SettingTimeScale()
    {
        if (GameTimeScale == TimeScale.one)
            GameTimeScale = TimeScale.two;
        else if (GameTimeScale == TimeScale.two)
            GameTimeScale = TimeScale.one;

        UIManager.Instance.UpdatexButtonUI();
    }

    // 외부에서 타임스케일을 설정하는 메소드
    public void SetTimeScaleExternal(float s)
    {
        Time.timeScale = s;
        GameTimeScale = (s <= 1f) ? TimeScale.one : TimeScale.two;
        UIManager.Instance.UpdatexButtonUI();
    }
}