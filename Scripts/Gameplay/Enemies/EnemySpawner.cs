using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public enum EnemyType
{
    HealthRegen,
    Invincible,
    MovementSpeed,
    Armor,
    Boss
}

/// <summary>한 라인의 스폰 큐와 적 레지스트리를 관리하고 호스트 상태를 주기적으로 묶어 전송합니다.</summary>
public class EnemySpawner : MonoBehaviour
{
    public PlayerController player; // 플레이어 골드 줄때 사용
    public int ownerIndex = -1; // 플레이어 인덱스 (소유자 인덱스, -1은 초기값으로 사용)
    public List<Enemy> enemies = new(); // 생성한 적 개체 리스트
    public int spawnerId; // 스포너 고유 id 값
    float distanceInFront = 2f; // 생성 거리
    public long killGold = 0; // 죽으면 나오는 골드
    public double enemyHp = 0; // 생성 적 HP설정
    public float enemyArmor = 0f; // 생성 적 방어력
    [Header("스폰 타이머 (밸런스)")]
    [SerializeField]
    private float spawnInterval = 2f; // 생성 주기(초)
    [SerializeField]
    private List<GameObject> enemyPrefabs; // 생성 프리팹 리스트
    [Header("웨이브 매니저")]
    [SerializeField]
    private WaveManager waveManager; // 웨이브 매니저 참조
    [HideInInspector]
    public WaveManager WaveManagerRef => waveManager;
    public bool IsActiveLane => player != null;

    public int ActiveEnemyCount
    {
        get
        {
            enemies.RemoveAll(enemy => enemy == null);
            return enemies.Count;
        }
    }

    [Header("적 HP바")]
    [SerializeField]
    private Canvas hpBarCanvas; // 적 HP바 캔버스
    [SerializeField]
    private Slider enemyHpSliderPrefab; // 적 HP바 프리팹
    [Header("적 상태 텍스트")]
    [SerializeField]
    private TMP_Text enemyStatusTextPrefab; // 적 상태 텍스트 프리팹
    float spawnTimer = 0f; // 재는 타이머
    public bool PatrolEnabled = true; // 브로드 캐스트용 경로 순찰 여부
    public Queue<EnemyType> spawnQueue = new(); // 현재 웨이브 당 스폰할 몬스터의 큐 (웨이브는 웨이브 매니저가 관리)
    // 적 ID 관리
    private int nextEnemyId = 0;
    private Dictionary<int, Enemy> enemyRegistry = new Dictionary<int, Enemy>();
    private static Dictionary<int, EnemySpawner> spawnerRegistry = new Dictionary<int, EnemySpawner>();
    private readonly List<EnemyStateMessage> stateBuffer = new();
    private const float NetworkStateInterval = 0.2f;
    private const int MaxStatesPerPacket = 8; // JSON UDP 단편화를 피하도록 MTU에 가까운 크기로 제한
    private float nextNetworkStateTime;
    public void InitializeSpawner(PlayerController playerController)
    {
        if (ownerIndex >= 0 && playerController.PlayerIndex != ownerIndex)
        {
            Debug.LogWarning($"[Spawner {spawnerId}] ownerIndex={ownerIndex}인데 " + $"주입하려는 플레이어 인덱스={playerController.PlayerIndex} 입니다. 무시.");
            return;
        }

        player = playerController;
    }

    private bool IsLocalOwner()
    {
        var gm = GameManager.Instance;
        // ownerIndex 라인의 플레이어가 존재하고, 그 플레이어가 이 클라의 로컬인지
        return gm != null && ownerIndex >= 0 && gm.players.Count > ownerIndex && gm.players[ownerIndex] != null && gm.players[ownerIndex].IsLocalPlayer;
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

    public void PromoteToAuthority()
    {
        int highestEnemyId = -1;
        foreach (var enemy in enemies)
        {
            if (enemy == null)
                continue;
            highestEnemyId = Mathf.Max(highestEnemyId, enemy.EnemyId);
            enemy.PromoteToAuthority();
        }

        nextEnemyId = Mathf.Max(nextEnemyId, highestEnemyId + 1);
        waveManager?.PromoteToAuthority();
    }

    // 레지스트리에 적 등록
    public void RegisteryEnemy(int id, Enemy enemy)
    {
        enemyRegistry[id] = enemy;
    }

    /// <summary>스포너 내부 ID로 적을 조회합니다. 다른 라인의 같은 ID와 혼동하지 않도록 합니다.</summary>
    public Enemy GetEnemyById(int id)
    {
        enemyRegistry.TryGetValue(id, out var enemy);
        return enemy;
    }

    public void UnregisterEnemy(int id, Enemy enemy)
    {
        if (enemyRegistry.TryGetValue(id, out var registered) && registered == enemy)
            enemyRegistry.Remove(id);
    }

    private void Start()
    {
        enemyArmor = GameManager.Instance.difficultyEnemyBonusArmor;
    }

    private void Update()
    {
        if (GameManager.Instance == null || !GameManager.Instance.IsHostPlayer)
            return;
        if (ownerIndex >= 0 && player == null)
            return;
        spawnTimer += Time.deltaTime;
        if (spawnTimer >= spawnInterval && spawnQueue.Count > 0)
        {
            EnemyType type = spawnQueue.Dequeue();
            SpawnEnemy(type);
            spawnTimer = 0f;
        }

        BroadcastEnemyStates();
    }

    public void StartWave(Wave waveData, int waveIndex, double hp, long rewardGold)
    {
        enemyHp = hp;
        killGold = rewardGold;
        spawnQueue.Clear();
        spawnTimer = spawnInterval;
        // waveData.spawns에 들어있는 SpawnInfo를 순회
        foreach (var spawnInfo in waveData.spawns)
        {
            for (int i = 0; i < spawnInfo.count; i++)
            {
                EnqueueEnemy(spawnInfo.type);
            }
        }
    }

    public void EnqueueEnemy(EnemyType type)
    {
        spawnQueue.Enqueue(type);
    }

    public void PrepareRemoteWave(Wave waveData)
    {
        spawnQueue.Clear();
        foreach (var spawnInfo in waveData.spawns)
        {
            for (int i = 0; i < spawnInfo.count; i++)
                spawnQueue.Enqueue(spawnInfo.type);
        }

        spawnTimer = spawnInterval;
    }

    public void ConsumeRemoteSpawn(EnemyType type)
    {
        if (spawnQueue.Count == 0)
            return;
        if (spawnQueue.Peek() == type)
            spawnQueue.Dequeue();
    }

    void SpawnEnemy(EnemyType type)
    {
        if (ownerIndex >= 0 && player == null)
            return;
        Vector3 spawnPos = transform.position + transform.forward * distanceInFront;
        Enemy enemy = Instantiate(enemyPrefabs[(int)type], spawnPos, transform.rotation).GetComponent<Enemy>();
        int enemyId = nextEnemyId++;
        enemy.SetHpBarPrefab(hpBarCanvas, enemyHpSliderPrefab, enemyStatusTextPrefab);
        enemy.InitializeEnemy(enemyId, enemyHp, enemyArmor, killGold, spawnerId, this, true); // 호스트 권한 적 초기화
        enemy.EnemyRouteSetting(player.PlayerIndex, this);
        RegisteryEnemy(enemyId, enemy);
        enemies.Add(enemy);
        BroadcastEnemyCount();
        EnemySpawnMessage msg = new EnemySpawnMessage
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
        string json = JsonUtility.ToJson(msg);
        UDPClient.Instance?.SendUDP(json, "ENEMY_SPAWN");
    }

    public void OnEnemyKilledLocal()
    {
        BroadcastEnemyCount();
    }

    private void BroadcastEnemyCount()
    {
        int count = ActiveEnemyCount;
        waveManager?.SetAuthoritativeEnemyCount(count);
        string json = JsonUtility.ToJson(new EnemyCountMessage { spawnerId = spawnerId, ownerIndex = ownerIndex, count = count });
        UDPClient.Instance?.SendUDP(json, "ENEMY_COUNT");
    }

    public void StopSpawning()
    {
        spawnQueue.Clear();
    }

    // 네트워크에서 받은 데이터로 재현
    public void SpawnEnemyRemote(EnemySpawnMessage data)
    {
        if (enemyRegistry.ContainsKey(data.enemyId))
            return; // 이미 해당 ID의 적이 존재하면 중복 생성 방지
        Enemy enemy = Instantiate(enemyPrefabs[(int)data.enemyType], data.position, Quaternion.identity).GetComponent<Enemy>();
        enemy.SetHpBarPrefab(hpBarCanvas, enemyHpSliderPrefab, enemyStatusTextPrefab);
        enemy.InitializeEnemy(data.enemyId, data.hp, data.enemyArmor, data.killGold, data.spawnerId, this, false);
        enemy.EnemyRouteSetting(data.ownerIndex, this);
        RegisteryEnemy(data.enemyId, enemy);
        enemies.Add(enemy);
        ConsumeRemoteSpawn(data.enemyType);
    }

    public Enemy SpawnEnemyRemoteFromState(EnemyStateMessage state)
    {
        if (enemyRegistry.TryGetValue(state.enemyId, out var existing))
            return existing;
        if ((int)state.enemyType < 0 || (int)state.enemyType >= enemyPrefabs.Count)
            return null;
        Enemy enemy = Instantiate(enemyPrefabs[(int)state.enemyType], state.position, state.rotation).GetComponent<Enemy>();
        enemy.SetHpBarPrefab(hpBarCanvas, enemyHpSliderPrefab, enemyStatusTextPrefab);
        enemy.InitializeEnemy(state.enemyId, state.maxHp, state.armor, state.killGold, spawnerId, this, false);
        enemy.EnemyRouteSetting(ownerIndex, this);
        RegisteryEnemy(state.enemyId, enemy);
        enemies.Add(enemy);
        return enemy;
    }

    /// <summary>호스트의 적 상태를 일정 주기로 묶어 보내 개별 오브젝트의 매 프레임 전송을 피합니다.</summary>
    private void BroadcastEnemyStates()
    {
        if (Time.unscaledTime < nextNetworkStateTime || enemies.Count == 0)
            return;
        nextNetworkStateTime = Time.unscaledTime + NetworkStateInterval;
        stateBuffer.Clear();
        foreach (var enemy in enemies)
        {
            if (enemy != null && enemy.IsAuthoritative)
                stateBuffer.Add(enemy.CreateNetworkState());
        }

        if (stateBuffer.Count == 0)
            return;
        for (int offset = 0; offset < stateBuffer.Count; offset += MaxStatesPerPacket)
        {
            int count = Mathf.Min(MaxStatesPerPacket, stateBuffer.Count - offset);
            var states = new EnemyStateMessage[count];
            stateBuffer.CopyTo(offset, states, 0, count);
            UDPClient.Instance?.SendUDP(JsonUtility.ToJson(new EnemyStateBatchMessage { spawnerId = spawnerId, activeEnemyCount = ActiveEnemyCount, states = states }), "ENEMY_STATE_BATCH");
        }
    }
}
