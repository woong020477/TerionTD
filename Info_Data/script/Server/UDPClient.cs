using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class UDPClient : MonoBehaviour
{
    private readonly ConcurrentQueue<System.Action> _mainThreadQueue = new();   // 메인 스레드 큐

    public static UDPClient Instance { get; private set; }

    private UdpClient client;                                                   // UDP 클라이언트 인스턴스
    private IPEndPoint serverEP;                                                // 서버 엔드포인트
    public int localPort = 9001;                                                // 로컬 포트
    public int serverPort = 9000;                                               // 서버 포트
    private bool isListening = false;                                           // 수신 중인지 여부
    bool connected;                                                             // 연결 상태

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        while (_mainThreadQueue.TryDequeue(out var action))                     // 메인 스레드 큐에서 작업을 꺼내 실행
            action?.Invoke();                                                   // null 체크 후 실행
    }

    // 메인 스레드에서 실행할 작업을 큐에 추가
    private void EnqueueOnMainThread(System.Action a)
    {
        if (a != null) _mainThreadQueue.Enqueue(a);
    }

    // UDP 연결 시작
    public void StartUDP(string serverIp)
    {
        if (connected) return;
        try
        {
            client = new UdpClient(localPort);
            serverEP = new IPEndPoint(IPAddress.Parse(serverIp), serverPort);
            connected = true;
            isListening = true;

            Debug.Log($"UDP 연결 시작: Server => {serverIp}:{serverPort}, localPort {localPort}");

            // 수신 루프 시작
            Task.Run(ReceiveLoop);
        }
        catch (Exception ex)
        {
            connected = false;
            isListening = false;
            client?.Close();
            client = null;
            Debug.LogError($"UDP 연결 실패: {ex.Message}");
        }
    }

    // UDP 연결 종료
    public void StopUDP()
    {
        Debug.Log("UDP 연결 종료");
        if (!connected) return;
        isListening = false;
        try { client?.Close(); } catch { }
        client = null;
        connected = false;
    }

    public void ExitRoom_UDP()
    {
        Debug.Log("UDP 연결 종료");
        if (!connected) return;
        isListening = false;
        try { client?.Close(); } catch { }
        client = null;
        connected = false;
        LoadGameController.Instance.LoadNextScene("LobbyScene"); // 방 나가기
    }

    // UDP 서버에 메시지 전송
    public void SendUDP(string json, string header)
    {
        if (client == null || serverEP == null) return;

        // "header|json" 형태로 전송
        string finalMsg = $"{header}|{json}";
        byte[] data = Encoding.UTF8.GetBytes(finalMsg);
        client.Send(data, data.Length, serverEP);
    }

    // UDP 수신 루프
    private async Task ReceiveLoop()
    {
        while (isListening)
        {
            try
            {
                var result = await client.ReceiveAsync();
                string msg = Encoding.UTF8.GetString(result.Buffer);
                Debug.Log($"[UDP 수신] {msg}");

                HandleUDPMessage(msg);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"UDP 수신 오류: {ex.Message}");
            }
        }
    }

    // UDP 메시지 처리 핸들러
    private void HandleUDPMessage(string message)
    {
        string header;
        string json;

        if (message.Contains("|"))
        {
            string[] parts = message.Split('|', 2);
            header = parts[0];
            json = parts.Length > 1 ? parts[1] : "{}";
        }
        else
        {
            var msgBase = JsonUtility.FromJson<UDPMessageBase>(message);
            header = msgBase?.action;
            json = message;
        }

        switch (header)
        {
            // 플레이어 정보 메시지 처리
            case "GAME_START":
                {
                    var m = JsonUtility.FromJson<GameStartMessage>(json);
                    EnqueueOnMainThread(() => GameManager.Instance.ApplyGameStart(m.startUnix));
                    break;
                }

            // 게임 시작 메시지 처리
            case "WAVE_START":
                {
                    var m = JsonUtility.FromJson<WaveStartMessage>(json);

                    EnqueueOnMainThread(() =>
                    {
                        var spawner = EnemySpawner.GetSpawner(m.spawnerId);
                        if (spawner == null) return;

                        // 담당자 검증(옵션): 서버 메세지의 ownerIndex와 스포너 설정이 일치하는지
                        if (spawner.ownerIndex >= 0 && spawner.ownerIndex != m.ownerIndex)
                            Debug.LogWarning($"스포너 {m.spawnerId} ownerIndex 미스매치 (spawner:{spawner.ownerIndex}, msg:{m.ownerIndex})");

                        var waveList = spawner.WaveManagerRef?.Waves();
                        if (waveList == null || m.waveIndex < 0 || m.waveIndex >= waveList.Count)
                        {
                            Debug.LogWarning($"잘못된 waveIndex {m.waveIndex}");
                            return;
                        }

                        // Host가 아닌 클라도 웨이브 시작은 동일하게 큐에 적을 넣어야 재현 가능
                        spawner.StartWave(waveList[m.waveIndex], m.waveIndex, m.hp, m.killGold);
                    });
                    break;
                }

            // 적 스폰 메시지 처리
            case "ENEMY_SPAWN":
                {
                    var data = JsonUtility.FromJson<EnemySpawnMessage>(json);   // json으로 파싱
                    EnqueueOnMainThread(() =>
                    {
                        var spawner = EnemySpawner.GetSpawner(data.spawnerId);      // 스포너 ID로 스포너 검색
                        if (spawner != null) spawner.SpawnEnemyRemote(data);
                    });
                    break;
                }

            // 적 데미지 메시지 처리
            case "ENEMY_DAMAGE":
                {
                    var msg = JsonUtility.FromJson<EnemyDamageMessage>(json);
                    EnqueueOnMainThread(() =>
                    {
                        var spawner = EnemySpawner.GetSpawner(msg.spawnerId);
                        var enemy = spawner?.GetEnemyById(msg.enemyId);
                        enemy?.ApplyDamageNetwork(msg.remainingHp);
                    });
                    break;
                }

            // 적 사망 메시지 처리
            case "ENEMY_DEATH":
                {
                    var msg = JsonUtility.FromJson<EnemyDeathMessage>(json);
                    EnqueueOnMainThread(() =>
                    {
                        var spawner = EnemySpawner.GetSpawner(msg.spawnerId);
                        var enemy = spawner?.GetEnemyById(msg.enemyId);
                        enemy?.Die(false); // 보상은 한쪽에서만
                    });
                    break;
                }

            // 보스 스킬 시작 메시지 처리
            case "BOSS_SKILL":
                {
                    var m = JsonUtility.FromJson<BossSkillMessage>(json);
                    EnqueueOnMainThread(() =>
                    {
                        var spawner = EnemySpawner.GetSpawner(m.spawnerId);
                        var enemy = spawner?.GetEnemyById(m.enemyId);   // 스포너별 레지스트리에서 조회
                        if (enemy == null) return;

                        if (m.start)
                            enemy.ReplicateBossSkillStart(m.pos, m.fwd, m.growDuration, m.maxDepth);
                        else
                            enemy.ReplicateBossSkillStopAndShoot(); // 사격/침묵 적용 + 리셋
                    });
                    break;
                }

            // 타워 발사 메시지 처리
            case "TOWER_FIRE":
                {
                    var fire = JsonUtility.FromJson<TowerFireMessage>(json);
                    EnqueueOnMainThread(() =>
                    {
                        // 스포너 레지스트리에서 스포너 찾기
                        Transform targetTr = null;
                        var spawner = EnemySpawner.GetSpawner(fire.targetSpawnerId);
                        var enemy = spawner?.GetEnemyById(fire.targetEnemyId);
                        if (enemy != null) targetTr = enemy.transform;

                        // 타워 찾기
                        var tower = TowerController.Towers.Find(t => t.TowerId == fire.towerId);
                        if (tower != null) tower.ReplicateFire(fire, targetTr); // 시그니처를 target 받도록
                    });
                    break;
                }

            // 타워베이스 건설 메시지 처리
            case "TOWER_BASE_PLACE":
                {
                    var m = JsonUtility.FromJson<TowerBasePlaceMessage>(json);
                    EnqueueOnMainThread(() => BuildingSystem.Instance.HandleRemoteBasePlace(m));
                    break;
                }

            // 타워 건설 메시지 처리
            case "TOWER_CREATE":
                {
                    var m = JsonUtility.FromJson<TowerCreateMessage>(json);
                    EnqueueOnMainThread(() => BuildingSystem.Instance.HandleTowerCreate(m)); // 내부에서 level 반영
                    break;
                }

            // 타워 연구소 연구 메시지 처리
            case "TOWER_LAB_UPGRADE":
                {
                    var m = JsonUtility.FromJson<TowerLabUpgradeMessage>(json);

                    // UI 레벨 업데이트
                    UIManager.Instance.SetLabLevel(m.ownerIndex, m.towerType, m.level);

                    // 해당 플레이어 소유 타워만 반영
                    var owner = GameManager.Instance.players[m.ownerIndex];
                    foreach (var tower in TowerController.Towers)
                    {
                        if (tower == null) continue;
                        if (tower.Owner == owner && tower.towerType == m.towerType)
                        {
                            // 역전 방지
                            if (m.level > tower.upgradeLevel)
                            {
                                tower.upgradeLevel = m.level;
                                tower.ApplyTowerStats(tower.upgradeLevel);
                                UIManager.Instance.UpdateBuildingStatus(tower);
                            }
                        }
                    }

                    // 필요 시 현재 열려있는 연구 UI 리프레시
                    UIManager.Instance.UpdateLabButtons();
                    break;
                }

            // 타워 시간 스케일 메시지 처리
            case "GAME_TIMESCALE":
                {
                    var m = JsonUtility.FromJson<GameTimeScaleMessage>(json);
                    GameManager.Instance.SetTimeScaleExternal(m.scale);
                    break;
                }

            // 타워 이동 메시지 처리
            case "TOWER_MOVE":
                {
                    var m = JsonUtility.FromJson<TowerMoveMessage>(json);
                    EnqueueOnMainThread(() => BuildingSystem.Instance.HandleTowerMove(m));
                    break;
                }

            // 게임 이벤트 메시지 처리
            case "GAME_EVENT_OBJ": EnqueueOnMainThread(() =>
                {
                    //GameManager.Instance.SpawnOBJLocally()); // 예시
                });
                break;
            case "GAME_EVENT_BUFF": EnqueueOnMainThread(() => 
                {
                    //GameManager.Instance.ApplyWaveBuffLocally()); // 예시
                });
                break;

            default:
                Debug.Log($"알 수 없는 UDP 메시지 수신: {header}");
                break;
        }
    }

    // 애플리케이션 종료 시 UDP 소켓 닫기
    private void OnApplicationQuit()
    {
        StopUDP();
    }
}

// UDP 메시지 베이스 클래스
[System.Serializable]
public class UDPMessageBase
{
    public string action;
}

// UDP 메시지 래퍼 클래스
[Serializable]
public class UdpEnvelope<T>
{
    public string action;
    public T payload;
}

// 게임 시작 메시지 구조체
[System.Serializable]
public struct GameStartMessage
{ 
    public double startUnix;        // UTC unix seconds
}

// 타워베이스 건설 메시지 구조체
[System.Serializable]
public struct TowerBasePlaceMessage
{
    public int ownerIndex;          // 플레이어 인덱스
    public int baseNetId;           // 베이스 네트워크 ID
    public Vector3 position;        // 건설 위치
}

// 타워 건설 메시지 구조체
public struct TowerCreateMessage
{
    public int ownerIndex;          // 플레이어 인덱스
    public int baseNetId;           // 베이스 네트워크 ID
    public TowerType towerType;     // 타워 타입
    public int level;               // 타워 레벨
}

// 타워 업그레이드 메시지 구조체
[System.Serializable]
public struct TowerUpgradeMessage
{
    public int baseNetId;           // 베이스 네트워크 ID
    public TowerType towerType;     // 타워 타입
    public int level;               // 타워 레벨
}

// 타워 연구 메시지 구조체
[System.Serializable]
public struct TowerLabUpgradeMessage
{
    public int ownerIndex;          // 연구한 플레이어
    public TowerType towerType;     // 연구한 타워 타입
    public int level;               // 새 연구 레벨
}

// 타워 발사 메시지 구조체
[System.Serializable]
public struct TowerFireMessage
{
    public int towerId;         // 타워 식별 ID
    public TowerType towerType; // Flame, Laser 등등
    public Vector3 position;    // 발사 위치
    public float damage;        // 공격력
    public bool isFirstShot;    // 머신건·멀티 로켓 등 첫발 여부
    public int targetEnemyId;   // 타겟 적 ID
    public int targetSpawnerId;
}

// 타워 이동 메시지 구조체
[System.Serializable]
public struct TowerMoveMessage
{
    public int ownerIndex;        // 이동 요청자
    public int baseNetId;         // 베이스 네트워크 ID
    public Vector3 basePosition;  // 베이스 최종 위치
    public bool hasTower;         // 베이스에 타워가 있었는가
    public Vector3 towerPosition; // 타워 최종 위치(있을 때만 유효)
}

// 적 스폰 메시지 구조체들
[System.Serializable]
public struct EnemySpawnMessage
{
    public int enemyId;         // 적 ID
    public int spawnerId;       // 스포너 ID
    public EnemyType enemyType; // 적 타입
    public long killGold;       // 적 처치 시 보상 골드
    public Vector3 position;    // 적의 스폰 위치
    public double hp;           // 적의 초기 HP
    public float enemyArmor;    // 적의 초기 방어력

    public int ownerIndex;      // 어떤 플레이어 경로로 이동할 지 결정하는 플레이어 인덱스
}

// 적 데미지 메시지 구조체
[System.Serializable]
public struct EnemyDamageMessage
{
    public int enemyId;         // 적 ID
    public int spawnerId;       // 스포너 ID
    public float damage;        // 데미지 양
    public double remainingHp;  // 적의 남은 HP
}

// 적 사망 메시지 구조체
[System.Serializable]
public struct EnemyDeathMessage
{
    public int enemyId;         // 적 ID
    public int spawnerId;       // 스포너 ID
}

[System.Serializable]
public struct BossSkillMessage
{
    public int spawnerId;
    public int enemyId;
    public bool start;
    public Vector3 pos;
    public Vector3 fwd;
    public float growDuration;
    public float maxDepth;
}

// 게임 시간 스케일 메시지 구조체
[System.Serializable]
public struct GameTimeScaleMessage
{
    public float scale;         // 현재 시간 스케일
}

// 웨이브 시작 메시지 구조체
[Serializable] 
public struct WaveStartMessage 
{ 
    public int spawnerId;       // 스포너 ID
    public int ownerIndex;      // 웨이브 시작한 플레이어 인덱스
    public int waveIndex;       // 웨이브 인덱스
    public double hp;           // 웨이브 시작 시 적의 초기 HP
    public long killGold;       // 웨이브 시작 시 적 처치 보상 골드
}

// 웨이브 클리어 메시지 구조체
[Serializable] 
public struct WaveClearMessage 
{ 
    public int spawnerId;       // 스포너 ID
    public int ownerIndex;      // 웨이브 클리어한 플레이어 인덱스
}
