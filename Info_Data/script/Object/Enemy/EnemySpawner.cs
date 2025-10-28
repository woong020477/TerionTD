using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public enum EnemyType           // 적 생성 타입 추가 해주고 enemyPrefabs리스트에 맞춰서 프리팹 넣어주면 됌 < 니가해
{
    HealthRegen,
    Invincible,
    MovementSpeed,
    Armor,
    Boss
}

public class EnemySpawner : MonoBehaviour
{
    public PlayerController player;                                     // 플레이어 골드 줄때 사용
    public int ownerIndex = -1;                                         // 플레이어 인덱스 (소유자 인덱스, -1은 초기값으로 사용)
    public List<Enemy> enemies = new();                                 // 생성한 적 개체 리스트
    public int spawnerId;                                               // 스포너 고유 id 값
    float distanceInFront = 2f;                                         // 생성 거리
    
    public long killGold = 0;                                           // 죽으면 나오는 골드
    public double enemyHp = 0;                                          // 생성 적 HP설정
    public float enemyArmor = 0f;                                       // 생성 적 방어력

    [Header("스폰 타이머 (밸런스)")]
    [SerializeField] private float spawnInterval = 2f;                  // 생성 주기(초)
    [SerializeField] private List<GameObject> enemyPrefabs;             // 생성 프리팹 리스트

    [Header("웨이브 매니저")]
    [SerializeField] private WaveManager waveManager;                   // 웨이브 매니저 참조
    [HideInInspector] public WaveManager WaveManagerRef => waveManager; // 웨이브 매니저 외부에서 참조용

    [Header("적 HP바")]
    [SerializeField] private Canvas hpBarCanvas;                        // 적 HP바 캔버스
    [SerializeField] private Slider enemyHpSliderPrefab;                // 적 HP바 프리팹

    [Header("적 상태 텍스트")]
    [SerializeField] private TMP_Text enemyStatusTextPrefab;            // 적 상태 텍스트 프리팹

    float spawnTimer = 0f;                                              // 재는 타이머
    public bool PatrolEnabled = true;                                   // 브로드 캐스트용 경로 순찰 여부

    public Queue<EnemyType> spawnQueue = new();                         // 현재 웨이브 당 스폰할 몬스터의 큐 (웨이브는 웨이브 매니저가 관리)

    // 적 ID 관리
    private int nextEnemyId = 0;
    private Dictionary<int, Enemy> enemyRegistry = new Dictionary<int, Enemy>();
    private static Dictionary<int, EnemySpawner> spawnerRegistry = new Dictionary<int, EnemySpawner>();

    public void InitializeSpawner(PlayerController playerController)
    {
        if (ownerIndex >= 0 && playerController.PlayerIndex != ownerIndex)
        {
            Debug.LogWarning($"[Spawner {spawnerId}] ownerIndex={ownerIndex}인데 " +
                             $"주입하려는 플레이어 인덱스={playerController.PlayerIndex} 입니다. 무시.");
            return;
        }
        player = playerController;                                   // 플레이어 컨트롤러 설정
        Debug.Log($"[Spawner {spawnerId}] Player 주입 완료 ← P{player.PlayerIndex}");
    }

    private bool IsLocalOwner()
    {
        var gm = GameManager.Instance;
        // ownerIndex 라인의 플레이어가 존재하고, 그 플레이어가 이 클라의 로컬인지
        return gm != null
            && ownerIndex >= 0
            && gm.players.Count > ownerIndex
            && gm.players[ownerIndex] != null
            && gm.players[ownerIndex].IsLocalPlayer;
    }

    private void Awake()
    {
        // 스포너 등록
        if (!spawnerRegistry.ContainsKey(spawnerId))
            spawnerRegistry[spawnerId] = this;
        else
            Debug.LogWarning($"[Spawner] spawnerId {spawnerId}가 중복 등록되었습니다.");
    }

    private void OnDestroy()
    {
        // 스포너 제거
        if (spawnerRegistry.ContainsKey(spawnerId))
            spawnerRegistry.Remove(spawnerId);
    }

    public static EnemySpawner GetSpawner(int id)
    {
        spawnerRegistry.TryGetValue(id, out var spawner);
        return spawner;
    }

    // 레지스트리에 적 등록
    public void RegisteryEnemy(int id, Enemy enemy)
    {
        enemyRegistry[id] = enemy;
    }

    // 적 ID로 적 검색
    public Enemy GetEnemyById(int id)
    {
        enemyRegistry.TryGetValue(id, out var enemy);
        return enemy;
    }
    private void Start()
    {
        enemyArmor = GameManager.Instance.difficultyEnemyBonusArmor;                                            // 적 방어력 설정 (게임 매니저의 난이도)
    }

    private void Update()
    {
        if (ownerIndex >= 0 && player == null) return;

        spawnTimer += Time.deltaTime;
        if (spawnTimer >= spawnInterval && spawnQueue.Count > 0)        
        {
            EnemyType type = spawnQueue.Dequeue();
            SpawnEnemy(type);
            spawnTimer = 0f;
        }
    }

    public void StartWave(Wave waveData, int waveIndex, double hp, long rewardGold)
    {
        enemyHp = hp;
        killGold = rewardGold;

        // waveData.spawns에 들어있는 SpawnInfo를 순회
        foreach (var spawnInfo in waveData.spawns)
        {
            for (int i = 0; i < spawnInfo.count; i++)
            {
                EnqueueEnemy(spawnInfo.type); // SpawnInfo.type에 맞춰 적 추가
            }
        }

        Debug.Log($"[Spawner {spawnerId}] Wave {waveIndex} 시작. hp={enemyHp}, gold={killGold}, q={spawnQueue.Count}");
    }

    public void EnqueueEnemy(EnemyType type)
    {
        spawnQueue.Enqueue(type);
    }

    void SpawnEnemy(EnemyType type)                                                                             // 로컬 환경에서 적 생성 메소드
    {
        if (ownerIndex >= 0 && player == null) return;                                                          // 플레이어가 없으면 생성하지 않음

        Vector3 spawnPos = transform.position + transform.forward * distanceInFront;                            // 생성 위치 계산
        Enemy enemy = Instantiate(enemyPrefabs[(int)type], spawnPos, transform.rotation)                        // 적 생성 후 Enemy 컴포넌트를 가져와 enemy 변수에 할당
            .GetComponent<Enemy>();
        int enemyId = nextEnemyId++;                                                                            // 적 ID 생성
        enemy.SetHpBarPrefab(hpBarCanvas, enemyHpSliderPrefab, enemyStatusTextPrefab);                          // 적 HP바, 상태텍스트 프리팹 설정
        enemy.InitializeEnemy(enemyId, enemyHp, enemyArmor, killGold, spawnerId, this);                         // 적 초기화
        enemy.EnemyRouteSetting(player.PlayerIndex, this);                                                      // 적 경로 설정 (플레이어 인덱스 사용)
        RegisteryEnemy(enemyId, enemy);                                                                         // 적 레지스트리에 등록
        enemies.Add(enemy);                                                                                     // 생성한 적 리스트에 추가

        if (IsLocalOwner())
            waveManager?.IncrementDeathCount();                                                                 // 웨이브 매니저에 적 수 증가 요청

        EnemySpawnMessage msg = new EnemySpawnMessage                                                           // 적 생성 메시지 구조체
        {
            enemyId = enemyId,
            spawnerId = spawnerId,
            enemyType = type,
            position = spawnPos,
            hp = enemyHp,
            enemyArmor = enemyArmor,
            killGold = killGold,
            ownerIndex = player.PlayerIndex
        };
        string json = JsonUtility.ToJson(msg);                                                                  // 메시지를 JSON으로 직렬화
        UDPClient.Instance?.SendUDP(json, "ENEMY_SPAWN");                                                       // 네트워크로 적 생성 메시지 전송
    }

    // 로컬에서 적이 죽었을 때 호출되는 메소드
    public void OnEnemyDiedLocal()
    {
        if (IsLocalOwner())
            waveManager?.DecrementDeathCount();
    }

    // 네트워크에서 받은 데이터로 재현
    public void SpawnEnemyRemote(EnemySpawnMessage data)
    {
        if (enemyRegistry.ContainsKey(data.enemyId))
            return;                                                                                             // 이미 해당 ID의 적이 존재하면 중복 생성 방지

        Enemy enemy = Instantiate(enemyPrefabs[(int)data.enemyType], data.position, Quaternion.identity)
                        .GetComponent<Enemy>();                                                                 // 적 프리팹에서 Enemy 컴포넌트를 가져옴

        enemy.SetHpBarPrefab(hpBarCanvas, enemyHpSliderPrefab, enemyStatusTextPrefab);                          // 적 HP바, 상태텍스트 프리팹 설정
        enemy.InitializeEnemy(data.enemyId, data.hp, data.enemyArmor, data.killGold, data.spawnerId, this);     // 적 초기화
        enemy.EnemyRouteSetting(data.ownerIndex, this);                                                         // 적 경로 설정 (플레이어 인덱스 사용)

        RegisteryEnemy(data.enemyId, enemy);                                                                    // 적 레지스트리에 등록
        enemies.Add(enemy);                                                                                     // 생성한 적 리스트에 추가
    }
}
