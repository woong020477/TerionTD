using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>방 단위 UDP 연결과 피어 경로를 관리하고, 수신 이벤트를 Unity 메인 스레드에 전달합니다.</summary>
public class UDPClient : MonoBehaviour
{
    private readonly ConcurrentQueue<System.Action> _mainThreadQueue = new(); // 메인 스레드 큐
    public static UDPClient Instance { get; private set; }

    private UdpClient client; // UDP 클라이언트 인스턴스
    private IPEndPoint serverEP; // 서버 엔드포인트
    public int localPort = 9001; // 로컬 포트
    public int serverPort = 9000; // 서버 포트
    private bool isListening = false; // 수신 중인지 여부
    bool connected; // 연결 상태
    private int currentRoomId;
    private int localUserId;
    private int hostUserId;
    private bool localIsHost;
    private float nextPeerHeartbeatTime;
    private readonly object peerLock = new();
    private readonly Dictionary<int, IPEndPoint> peerEndpoints = new();
    private readonly HashSet<int> directlyReachablePeerIds = new();
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

    /// <summary>수신 스레드가 예약한 작업을 메인 스레드에서 실행하고 피어 연결 유지를 처리합니다.</summary>
    private void Update()
    {
        while (_mainThreadQueue.TryDequeue(out var action)) // 메인 스레드 큐에서 작업을 꺼내 실행
            action?.Invoke();
        if (connected && Time.unscaledTime >= nextPeerHeartbeatTime)
        {
            nextPeerHeartbeatTime = Time.unscaledTime + 2f;
            SendControl("P2P_HEARTBEAT", "{}");
            SendPeerHello();
        }
    }

    // 메인 스레드에서 실행할 작업을 큐에 추가
    private void EnqueueOnMainThread(System.Action a)
    {
        if (a != null)
            _mainThreadQueue.Enqueue(a);
    }

    /// <summary>
    /// 지정한 게임 서버에 UDP 엔드포인트를 만들고 현재 방 번호를 등록합니다.
    /// 로컬에서 여러 클라이언트를 실행할 때 기본 포트가 사용 중이면 임시 포트를 자동으로 선택합니다.
    /// </summary>
    public void StartUDP(string serverIp, int roomId, int userId, bool isHost, int remotePort = 9000)
    {
        if (connected)
            return;
        try
        {
            serverPort = remotePort;
            try
            {
                client = new UdpClient(localPort);
            }
            catch (SocketException)when (localPort != 0)
            {
                client = new UdpClient(0);
                Debug.LogWarning($"UDP 로컬 포트 {localPort}가 사용 중이어서 자동 포트를 사용합니다.");
            }

            serverEP = new IPEndPoint(IPAddress.Parse(serverIp), serverPort);
            currentRoomId = roomId;
            localUserId = userId;
            hostUserId = isHost ? userId : 0;
            localIsHost = isHost;
            connected = true;
            isListening = true;
            Debug.Log($"UDP 연결 시작: Server => {serverIp}:{serverPort}, localPort {localPort}");
            // 수신 루프 시작
            Task.Run(ReceiveLoop);
            SendControl("REGISTER", JsonUtility.ToJson(new UdpRoomRegistrationMessage { roomId = roomId, userId = userId, isHost = isHost }));
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
        if (!connected)
            return;
        try
        {
            SendControl("UNREGISTER", JsonUtility.ToJson(new UdpRoomRegistrationMessage { roomId = currentRoomId, userId = localUserId, isHost = localIsHost }));
        }
        catch (SocketException ex)
        {
            Debug.LogWarning($"UDP 등록 해제 전송 실패: {ex.Message}");
        }
        finally
        {
            isListening = false;
            try
            {
                client?.Close();
            }
            catch
            {
            }

            client = null;
            connected = false;
            currentRoomId = 0;
            localUserId = 0;
            hostUserId = 0;
            lock (peerLock)
            {
                peerEndpoints.Clear();
                directlyReachablePeerIds.Clear();
            }
        }
    }

    public void ExitRoom_UDP()
    {
        if (currentRoomId > 0)
            AuthManager.Instance?.SendExitRoom(currentRoomId);
        StopUDP();
        AuthManager.Instance?.ClearCurrentRoomInfo();
        LoadGameController.Instance.LoadNextScene("LobbyScene");
    }

    /// <summary>모든 원격 피어의 직접 연결이 확인되면 P2P로, 아니면 방 중계 서버로 보냅니다.</summary>
    public void SendUDP(string json, string header)
    {
        if (client == null || serverEP == null)
            return;
        string finalMsg = $"{header}|{json}";
        byte[] data = Encoding.UTF8.GetBytes(finalMsg);
        IPEndPoint[] targets;
        bool allRemotePeersReachable;
        lock (peerLock)
        {
            var remotePeers = peerEndpoints.Where(pair => pair.Key != localUserId).ToArray();
            targets = remotePeers.Select(pair => pair.Value).ToArray();
            allRemotePeersReachable = remotePeers.Length > 0 && remotePeers.All(pair => directlyReachablePeerIds.Contains(pair.Key));
        }

        // PEER_LIST는 후보 주소일 뿐 직접 연결 성공을 보장하지 않습니다.
        // 모든 원격 피어의 HELLO를 실제로 받기 전까지는 서버 중계만 사용해 패킷 중복과 유실을 막습니다.
        if (!allRemotePeersReachable)
        {
            SendRaw(data, serverEP);
            return;
        }

        foreach (var target in targets)
            SendRaw(data, target);
    }

    private void SendControl(string header, string json)
    {
        if (client == null || serverEP == null)
            return;
        byte[] data = Encoding.UTF8.GetBytes($"{header}|{json}");
        SendRaw(data, serverEP);
    }

    private void SendRaw(byte[] data, IPEndPoint target)
    {
        try
        {
            client?.Send(data, data.Length, target);
        }
        catch (SocketException ex)when (connected)
        {
            Debug.LogWarning($"UDP 전송 실패: {ex.SocketErrorCode}");
        }
    }

    private void SendPeerHello()
    {
        byte[] hello = Encoding.UTF8.GetBytes("P2P_HELLO|{}");
        IPEndPoint[] targets;
        lock (peerLock)
            targets = peerEndpoints.Where(pair => pair.Key != localUserId).Select(pair => pair.Value).ToArray();
        foreach (var target in targets)
            SendRaw(hello, target);
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
                HandleUDPMessage(msg, result.RemoteEndPoint);
            }
            catch (Exception ex)
            {
                if (isListening)
                    Debug.LogWarning($"UDP 수신 오류: {ex.Message}");
            }
        }
    }

    /// <summary>메시지 종류를 판별하고 Unity 오브젝트를 만지는 처리는 메인 스레드 큐로 넘깁니다.</summary>
    private void HandleUDPMessage(string message, IPEndPoint sender)
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

        if (header == "PEER_LIST")
        {
            if (serverEP != null && sender.Equals(serverEP))
                ApplyPeerList(json);
            return;
        }

        if (header == "P2P_HELLO")
        {
            MarkDirectPeerReachable(sender);
            return;
        }

        if (!IsGameplaySenderAllowed(header, sender))
            return;
        switch (header)
        {
            case "HOST_CHANGED":
            {
                if (serverEP == null || !sender.Equals(serverEP))
                    break;
                var change = JsonUtility.FromJson<UdpHostChangedMessage>(json);
                ApplyHostChange(change.hostUserId, change.departedUserId);
                break;
            }

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
                    if (spawner == null)
                        return;
                    // 담당자 검증(옵션): 서버 메세지의 ownerIndex와 스포너 설정이 일치하는지
                    if (spawner.ownerIndex >= 0 && spawner.ownerIndex != m.ownerIndex)
                        Debug.LogWarning($"스포너 {m.spawnerId} ownerIndex 미스매치 (spawner:{spawner.ownerIndex}, msg:{m.ownerIndex})");
                    var waveManager = spawner.WaveManagerRef;
                    if (waveManager == null || m.waveIndex < 0)
                    {
                        Debug.LogWarning($"잘못된 waveIndex {m.waveIndex}");
                        return;
                    }

                    waveManager.ApplyRemoteWaveStart(m.waveIndex, m.hp, m.killGold);
                });
                break;
            }

            case "WAVE_CLEAR":
            {
                var m = JsonUtility.FromJson<WaveClearMessage>(json);
                EnqueueOnMainThread(() =>
                {
                    var spawner = EnemySpawner.GetSpawner(m.spawnerId);
                    spawner?.WaveManagerRef?.ApplyRemoteWaveClear(m.isFinalWave);
                });
                break;
            }

            case "PLAYER_MATCH_RESULT":
            {
                var m = JsonUtility.FromJson<PlayerMatchResultMessage>(json);
                EnqueueOnMainThread(() =>
                {
                    if (GameManager.Instance == null || GameManager.Instance.IsHostPlayer)
                        return;
                    GameManager.Instance.ApplyPlayerResult(m.ownerIndex, m.result);
                });
                break;
            }

            case "MATCH_COMPLETE":
            {
                var completion = JsonUtility.FromJson<MatchCompleteMessage>(json);
                EnqueueOnMainThread(() =>
                {
                    if (GameManager.Instance == null || GameManager.Instance.IsHostPlayer)
                        return;
                    if (completion.results != null)
                    {
                        foreach (var result in completion.results)
                            GameManager.Instance.ApplyPlayerResult(result.ownerIndex, result.result);
                    }

                    GameManager.Instance.ApplyMatchComplete();
                });
                break;
            }

            // 적 스폰 메시지 처리
            case "ENEMY_SPAWN":
            {
                var data = JsonUtility.FromJson<EnemySpawnMessage>(json);
                EnqueueOnMainThread(() =>
                {
                    if (GameManager.Instance != null && GameManager.Instance.IsHostPlayer)
                        return;
                    var spawner = EnemySpawner.GetSpawner(data.spawnerId);
                    if (spawner != null)
                        spawner.SpawnEnemyRemote(data);
                });
                break;
            }

            // 호스트가 계산한 적 Transform을 비호스트 표현 객체에 반영한다.
            case "ENEMY_STATE_BATCH":
            {
                var batch = JsonUtility.FromJson<EnemyStateBatchMessage>(json);
                EnqueueOnMainThread(() =>
                {
                    if (GameManager.Instance != null && GameManager.Instance.IsHostPlayer)
                        return;
                    var spawner = EnemySpawner.GetSpawner(batch.spawnerId);
                    if (spawner == null || batch.states == null)
                        return;
                    foreach (var state in batch.states)
                    {
                        var enemy = spawner.GetEnemyById(state.enemyId);
                        if (enemy == null)
                            enemy = spawner.SpawnEnemyRemoteFromState(state);
                        enemy?.ApplyNetworkState(state);
                    }

                    spawner.WaveManagerRef?.ApplyRemoteEnemyCount(batch.activeEnemyCount);
                });
                break;
            }

            // 비호스트 공격은 요청만 보내고, 호스트가 확정 HP를 다시 전송한다.
            case "ENEMY_HIT_REQUEST":
            {
                var request = JsonUtility.FromJson<EnemyHitRequestMessage>(json);
                EnqueueOnMainThread(() =>
                {
                    if (GameManager.Instance == null || !GameManager.Instance.IsHostPlayer)
                        return;
                    if (request.attackerIndex < 0 || request.attackerIndex >= GameManager.Instance.players.Count)
                        return;
                    var spawner = EnemySpawner.GetSpawner(request.spawnerId);
                    var enemy = spawner?.GetEnemyById(request.enemyId);
                    if (enemy != null && enemy.IsAlive)
                        enemy.ApplyHitRequest(request.damage, request.trueDamage, request.attackerIndex, request.stunChance, request.stunDuration, request.presentationDuration);
                });
                break;
            }

            case "ENEMY_STUN_REQUEST":
            {
                var request = JsonUtility.FromJson<EnemyStunRequestMessage>(json);
                EnqueueOnMainThread(() =>
                {
                    if (GameManager.Instance == null || !GameManager.Instance.IsHostPlayer)
                        return;
                    var spawner = EnemySpawner.GetSpawner(request.spawnerId);
                    var enemy = spawner?.GetEnemyById(request.enemyId);
                    enemy?.ApplyStunRequest(request.duration);
                });
                break;
            }

            // 적 데미지 메시지 처리
            case "ENEMY_DAMAGE":
            {
                var msg = JsonUtility.FromJson<EnemyDamageMessage>(json);
                EnqueueOnMainThread(() =>
                {
                    if (GameManager.Instance != null && GameManager.Instance.IsHostPlayer)
                        return;
                    var spawner = EnemySpawner.GetSpawner(msg.spawnerId);
                    var enemy = spawner?.GetEnemyById(msg.enemyId);
                    if (enemy != null)
                        enemy.ApplyDamageNetwork(msg.remainingHp, msg.presentationDuration);
                });
                break;
            }

            // 적 사망 메시지 처리
            case "ENEMY_DEATH":
            {
                var msg = JsonUtility.FromJson<EnemyDeathMessage>(json);
                EnqueueOnMainThread(() =>
                {
                    if (GameManager.Instance != null && GameManager.Instance.IsHostPlayer)
                        return;
                    var spawner = EnemySpawner.GetSpawner(msg.spawnerId);
                    var enemy = spawner?.GetEnemyById(msg.enemyId);
                    enemy?.ApplyDeathNetwork(msg.killerPlayerIndex);
                });
                break;
            }

            case "ENEMY_COUNT":
            {
                var msg = JsonUtility.FromJson<EnemyCountMessage>(json);
                EnqueueOnMainThread(() =>
                {
                    if (GameManager.Instance != null && GameManager.Instance.IsHostPlayer)
                        return;
                    var spawner = EnemySpawner.GetSpawner(msg.spawnerId);
                    spawner?.WaveManagerRef?.ApplyRemoteEnemyCount(msg.count);
                });
                break;
            }

            // 보스 스킬 시작 메시지 처리
            case "BOSS_SKILL":
            {
                var m = JsonUtility.FromJson<BossSkillMessage>(json);
                EnqueueOnMainThread(() =>
                {
                    if (GameManager.Instance != null && GameManager.Instance.IsHostPlayer)
                        return;
                    var spawner = EnemySpawner.GetSpawner(m.spawnerId);
                    var enemy = spawner?.GetEnemyById(m.enemyId);
                    if (enemy == null)
                        return;
                    if (m.start)
                        enemy.ReplicateBossSkillStart(m.pos, m.fwd, m.growDuration, m.maxDepth);
                    else
                        enemy.ReplicateBossSkillStop(m.cancelled); // 호스트에서 취소되지 않은 경우만 사격/침묵 적용
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
                    var spawner = EnemySpawner.GetSpawner(fire.targetSpawnerId);
                    var enemy = spawner?.GetEnemyById(fire.targetEnemyId);
                    // 사망/스폰보다 늦게 온 발사를 다른 적에게 재현하지 않습니다.
                    if (enemy == null || !enemy.IsAlive)
                        return;
                    // 소유자와 로컬 ID 조합으로 정확한 타워를 찾는다.
                    if (TowerController.TryGetNetworkTower(fire.ownerIndex, fire.towerId, out var tower) && tower != null)
                        tower.ReplicateFire(fire, enemy.transform);
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
                EnqueueOnMainThread(() => BuildingSystem.Instance.HandleTowerCreate(m));
                break;
            }

            // 타워 연구소 연구 메시지 처리
            case "TOWER_LAB_UPGRADE":
            {
                var m = JsonUtility.FromJson<TowerLabUpgradeMessage>(json);
                EnqueueOnMainThread(() =>
                {
                    if (GameManager.Instance == null || UIManager.Instance == null)
                        return;
                    if (m.ownerIndex < 0 || m.ownerIndex >= GameManager.Instance.players.Count)
                        return;
                    UIManager.Instance.SetLabLevel(m.ownerIndex, m.towerType, m.level);
                    var remoteOwner = GameManager.Instance.players[m.ownerIndex];
                    foreach (var tower in TowerController.Towers)
                    {
                        if (tower == null || tower.Owner != remoteOwner || tower.towerType != m.towerType)
                            continue;
                        if (m.level > tower.upgradeLevel)
                        {
                            tower.upgradeLevel = m.level;
                            tower.ApplyTowerStats(tower.upgradeLevel);
                            UIManager.Instance.RefreshBuildingStatusIfSelected(tower);
                        }
                    }

                    UIManager.Instance.UpdateLabButtons();
                });
                break;
            }

            // 타워 시간 스케일 메시지 처리
            case "GAME_TIMESCALE":
            {
                var m = JsonUtility.FromJson<GameTimeScaleMessage>(json);
                EnqueueOnMainThread(() => GameManager.Instance?.SetTimeScaleExternal(m.scale));
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
            case "GAME_EVENT_OBJ":
                EnqueueOnMainThread(() =>
                {
                //GameManager.Instance.SpawnOBJLocally()); // 예시
                });
                break;
            case "GAME_EVENT_BUFF":
                EnqueueOnMainThread(() =>
                {
                //GameManager.Instance.ApplyWaveBuffLocally()); // 예시
                });
                break;
            default:
                break;
        }
    }

    /// <summary>적·웨이브·결과처럼 호스트만 확정할 수 있는 이벤트의 송신자를 검사합니다.</summary>
    private bool IsGameplaySenderAllowed(string header, IPEndPoint sender)
    {
        // 엔드포인트 목록이 준비되기 전 서버가 중계한 초기 패킷은 허용합니다.
        if (serverEP != null && sender.Equals(serverEP))
            return true;
        int senderUserId = 0;
        lock (peerLock)
        {
            foreach (var peer in peerEndpoints)
            {
                if (!peer.Value.Equals(sender))
                    continue;
                senderUserId = peer.Key;
                break;
            }
        }

        if (senderUserId <= 0)
            return false;
        // 웨이브·적 상태·결과는 호스트만 확정할 수 있다. 건설/공격 요청은 각 피어가 직접 배포한다.
        return header switch
        {
            "GAME_START" or "WAVE_START" or "WAVE_CLEAR" or "PLAYER_MATCH_RESULT" or "MATCH_COMPLETE" or "ENEMY_SPAWN" or "ENEMY_STATE_BATCH" or "ENEMY_DAMAGE" or "ENEMY_DEATH" or "ENEMY_COUNT" or "BOSS_SKILL" or "GAME_TIMESCALE" => senderUserId == hostUserId,
            _ => true
        };
    }

    private void ApplyPeerList(string json)
    {
        var list = JsonUtility.FromJson<UdpPeerListMessage>(json);
        if (list == null || list.roomId != currentRoomId || list.peers == null)
            return;
        int previousHost = hostUserId;
        lock (peerLock)
        {
            var previousEndpoints = new Dictionary<int, IPEndPoint>(peerEndpoints);
            peerEndpoints.Clear();
            foreach (var peer in list.peers)
            {
                if (peer.userId <= 0 || peer.port <= 0 || !IPAddress.TryParse(peer.address, out var address))
                    continue;
                peerEndpoints[peer.userId] = new IPEndPoint(address, peer.port);
            }

            directlyReachablePeerIds.RemoveWhere(peerId => !peerEndpoints.TryGetValue(peerId, out var currentEndpoint) || !previousEndpoints.TryGetValue(peerId, out var previousEndpoint) || !currentEndpoint.Equals(previousEndpoint));
            hostUserId = list.hostUserId;
            localIsHost = localUserId > 0 && localUserId == hostUserId;
        }

        SendPeerHello();
        if (previousHost > 0 && previousHost != hostUserId)
            EnqueueOnMainThread(() => GameManager.Instance?.ApplyHostChanged(hostUserId, previousHost));
    }

    private void MarkDirectPeerReachable(IPEndPoint sender)
    {
        lock (peerLock)
        {
            foreach (var peer in peerEndpoints)
            {
                if (peer.Key == localUserId || !peer.Value.Equals(sender))
                    continue;
                directlyReachablePeerIds.Add(peer.Key);
                return;
            }
        }
    }

    /// <summary>새 호스트와 이탈 피어를 연결 상태에 반영하고 게임 권한 전환을 요청합니다.</summary>
    public void ApplyHostChange(int newHostUserId, int departedUserId)
    {
        if (newHostUserId <= 0)
            return;
        lock (peerLock)
        {
            hostUserId = newHostUserId;
            localIsHost = localUserId == newHostUserId;
        }

        EnqueueOnMainThread(() => GameManager.Instance?.ApplyHostChanged(newHostUserId, departedUserId));
    }

    // 애플리케이션 종료 시 UDP 소켓 닫기
    private void OnApplicationQuit()
    {
        StopUDP();
    }

    private void OnDestroy()
    {
        StopUDP();
        if (Instance == this)
            Instance = null;
    }
}
