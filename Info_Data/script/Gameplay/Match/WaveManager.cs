using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>플레이어 한 라인의 라운드·휴식·생존 수를 관리합니다. 진행 판정은 호스트가 수행합니다.</summary>
public class WaveManager : MonoBehaviour
{
    // 웨이브 매니저 클래스
    [SerializeField]
    private int ownerIndex; // 웨이브 매니저 소유자 인덱스 (플레이어 인덱스)
    [SerializeField]
    private EnemySpawner spawner; // 매니저와 연결 할 스포너
    [SerializeField]
    private List<Wave> waveList; // 웨이브 소환 리스트
    [SerializeField]
    private List<WaveData> _waves; // 웨이브
    private int currentWaveIndex = 0; // 현재 웨이브 값
    /* -----  타이머 관련 ------ */
    private float waveRestTimer = 0; // 대기시간 용 타이머
    public float currentWaveTimer = 0; // 현재 웨이브 타이머
    private float WaveLimitTimer = 120f; // 웨이브 제한 시간 타이머
    private float waveStartTimer = 5f; // 웨이브 시작 시간
    [Header("UI")]
    [SerializeField]
    private TextMeshProUGUI roundText; // UI 웨이브 카운터 텍스트
    [SerializeField]
    private TextMeshProUGUI timerText;
    [SerializeField]
    private Slider timeslider;
    [SerializeField]
    private TextMeshProUGUI deathCountText;
    [Header("한계 데스카운트")]
    public int maxDeathCount; // 최대 DeathCount값
    // 이번 라운드 스폰 등록 완료 플래그
    private bool enqueuedAll = false;
    /* ------- 데이터(Json)파일 -------*/
    public TextAsset jsonFile;
    // UI에 쓸 변수
    private float remainingTime;
    private int activeEnemyCount = 0;
    public bool IsLaneFinished { get; private set; }
    private bool IsLocalLane => GameManager.Instance != null && GameManager.Instance.LocalPlayer != null && GameManager.Instance.LocalPlayer.PlayerIndex == ownerIndex;

    WaveState state = WaveState.Waiting; // 웨이브 열거형 클래스
    private void Awake()
    {
        waveList = Waves();
        maxDeathCount = 80;
    }

    private void Start()
    {
        if (jsonFile != null)
        {
            var wrapper = JsonUtility.FromJson<WaveStatusWrapper>(jsonFile.text);
            _waves = wrapper.EnemyStatus;
        }

        UpdateCounter();
    }

    /// <summary>호스트는 라운드 경계를 판정하고 비호스트는 수신 상태를 기준으로 표시 시간을 진행합니다.</summary>
    private void Update()
    {
        if (IsLocalLane)
        {
            UpdateTimerUI();
            UpdateSlider();
            UpdateCounter();
        }

        UpdateDeathCountUI();
        bool isAuthority = GameManager.Instance != null && GameManager.Instance.IsHostPlayer;
        if (!isAuthority)
        {
            if (state == WaveState.Running)
                currentWaveTimer += Time.deltaTime;
            else
                waveRestTimer += Time.deltaTime;
            return;
        }

        if (GameManager.Instance.IsMatchFinished || IsLaneFinished || !spawner.IsActiveLane)
            return;
        if (state == WaveState.Running)
        {
            currentWaveTimer += Time.deltaTime;
            if (enqueuedAll && spawner.spawnQueue.Count == 0 && activeEnemyCount == 0)
            {
                ResolveRoundBoundary();
            }
            else if (currentWaveTimer >= WaveLimitTimer)
            {
                ResolveRoundBoundary();
            }
        }
        else if (state == WaveState.Waiting)
        {
            waveRestTimer += Time.deltaTime;
            if (waveRestTimer >= waveStartTimer)
            {
                StartWave();
            }
        }
    }

    /// <summary>다음 라운드 시작 직전에 생존 수 한도를 다시 검사하고 스폰 조건을 설정합니다.</summary>
    void StartWave()
    {
        if (GameManager.Instance == null || !GameManager.Instance.IsHostPlayer)
            return;
        if (currentWaveIndex >= waveList.Count)
            return;
        // 휴식이 끝나는 정확한 시점에 실제 생존 수를 다시 읽는다.
        // 이전 라운드 적이 한도까지 누적된 라인은 새 라운드를 시작하지 않고 개별 패배 처리한다.
        activeEnemyCount = spawner.ActiveEnemyCount;
        if (currentWaveIndex > 0 && activeEnemyCount >= maxDeathCount)
        {
            GameManager.Instance.ReportPlayerResult(ownerIndex, MatchResult.Defeat);
            return;
        }

        var game = GameManager.Instance;
        // HP/Gold 세팅(내 라인만)
        if (_waves != null && currentWaveIndex < _waves.Count)
        {
            double baseHp = _waves[currentWaveIndex].HP;
            spawner.enemyHp = baseHp * (double)game.difficultyEnemyBonusHealth;
            spawner.killGold = _waves[currentWaveIndex].KillGold;
            // 선택: UI 동기화용 브로드캐스트
            UDPClient.Instance?.SendUDP(JsonUtility.ToJson(new WaveStartMessage { spawnerId = spawner.spawnerId, ownerIndex = ownerIndex, waveIndex = currentWaveIndex, hp = spawner.enemyHp, killGold = spawner.killGold }), "WAVE_START");
        }

        spawner.StartWave(waveList[currentWaveIndex], currentWaveIndex, spawner.enemyHp, spawner.killGold);
        enqueuedAll = true;
        state = WaveState.Running;
        currentWaveTimer = 0f;
        currentWaveIndex++;
        UpdateCounter();
    }

    private void CompleteWave()
    {
        bool isFinalWave = currentWaveIndex >= waveList.Count;
        // 제한 시간 종료 시 아직 큐에 남은 이전 라운드 적은 휴식 시간에 추가 생성하지 않습니다.
        // 이미 필드에 나온 적은 제거하지 않고 다음 라운드까지 그대로 유지합니다.
        spawner.StopSpawning();
        state = isFinalWave ? WaveState.Finished : WaveState.Waiting;
        IsLaneFinished = isFinalWave;
        waveRestTimer = 0f;
        currentWaveTimer = 0f;
        enqueuedAll = false;
        UpdateCounter();
        UDPClient.Instance?.SendUDP(JsonUtility.ToJson(new WaveClearMessage { spawnerId = spawner.spawnerId, ownerIndex = ownerIndex, isFinalWave = isFinalWave }), "WAVE_CLEAR");
        if (isFinalWave)
            GameManager.Instance.ReportPlayerResult(ownerIndex, MatchResult.Victory);
    }

    /// <summary>전멸 또는 제한 시간 종료 시 해당 라인의 생존 수를 기준으로 패배와 휴식을 결정합니다.</summary>
    private void ResolveRoundBoundary()
    {
        activeEnemyCount = spawner.ActiveEnemyCount;
        if (activeEnemyCount >= maxDeathCount)
        {
            GameManager.Instance.ReportPlayerResult(ownerIndex, MatchResult.Defeat);
            return;
        }

        CompleteWave();
    }

    public void ApplyRemoteWaveStart(int waveIndex, double hp, long rewardGold)
    {
        if (GameManager.Instance != null && GameManager.Instance.IsHostPlayer)
            return;
        spawner.enemyHp = hp;
        spawner.killGold = rewardGold;
        currentWaveIndex = Mathf.Max(currentWaveIndex, waveIndex + 1);
        state = WaveState.Running;
        waveRestTimer = 0f;
        currentWaveTimer = 0f;
        enqueuedAll = false;
        if (waveIndex < waveList.Count)
            spawner.PrepareRemoteWave(waveList[waveIndex]);
        UpdateCounter();
    }

    public void ApplyRemoteWaveClear(bool isFinalWave)
    {
        if (GameManager.Instance != null && GameManager.Instance.IsHostPlayer)
            return;
        state = isFinalWave ? WaveState.Finished : WaveState.Waiting;
        IsLaneFinished = isFinalWave;
        waveRestTimer = 0f;
        currentWaveTimer = 0f;
        enqueuedAll = false;
        UpdateCounter();
    }

    // 회전 순서 정의
    private static readonly EnemyType[] Rotation =
    {
        EnemyType.HealthRegen,
        EnemyType.Invincible,
        EnemyType.MovementSpeed,
        EnemyType.Armor
    };
    // n라운드의 일반몹 타입을 계산 (10의 배수면 null 의미)
    public static EnemyType? GetNormalTypeForWave(int wave)
    {
        if (wave <= 0)
            return null;
        if (wave % 10 == 0)
            return null;
        int segment = (wave - 1) / 10;
        int idx = segment % Rotation.Length;
        return Rotation[idx];
    }

    public List<Wave> Waves(int maxWave = 100)
    {
        var waves = new List<Wave>(maxWave);
        for (int wave = 1; wave <= maxWave; wave++)
        {
            var w = new Wave
            {
                spawns = new List<SpawnInfo>()
            };
            if (wave % 10 == 0)
            {
                // 10 라운드 마다 보스 1마리
                w.spawns.Add(new SpawnInfo { type = EnemyType.Boss, count = 1 });
            }
            else
            {
                // 타입 로테이션 10라마다 회전하며 30마리
                var t = GetNormalTypeForWave(wave) ?? EnemyType.HealthRegen;
                w.spawns.Add(new SpawnInfo { type = t, count = 30 });
            }

            waves.Add(w);
        }

        return waves;
    }

    public void SetAuthoritativeEnemyCount(int count)
    {
        if (GameManager.Instance == null || !GameManager.Instance.IsHostPlayer)
            return;
        activeEnemyCount = Mathf.Max(0, count);
        UpdateDeathCountUI();
    }

    public void ApplyRemoteEnemyCount(int count)
    {
        if (GameManager.Instance != null && GameManager.Instance.IsHostPlayer)
            return;
        activeEnemyCount = Mathf.Max(0, count);
        UpdateDeathCountUI();
    }

    /// <summary>호스트 교체 후 기존 라운드와 생존 적 상태를 새 권한으로 이어받습니다.</summary>
    public void PromoteToAuthority()
    {
        activeEnemyCount = spawner.ActiveEnemyCount;
        enqueuedAll = state == WaveState.Running;
        UpdateDeathCountUI();
        if (IsLocalLane)
        {
            UpdateTimerUI();
            UpdateSlider();
            UpdateCounter();
        }
    }

    public void ApplyMatchFinished(bool victory, bool showLocalResult)
    {
        state = WaveState.Finished;
        IsLaneFinished = true;
        StopAllCoroutines();
        spawner.StopSpawning();
        if (!showLocalResult)
            return;
        if (roundText != null)
            roundText.text = victory ? "승리" : "패배";
        if (timerText != null)
            timerText.text = "다른 플레이어 진행 대기 중";
        if (timeslider != null)
            timeslider.value = 0f;
    }

    void UpdateTimerUI()
    {
        if (timerText == null)
            return;
        switch (state)
        {
            case WaveState.Running:
                remainingTime = Mathf.Clamp(WaveLimitTimer - currentWaveTimer, 0f, WaveLimitTimer);
                break;
            case WaveState.Waiting:
                remainingTime = Mathf.Clamp(waveStartTimer - waveRestTimer, 0f, waveStartTimer);
                break;
            case WaveState.Finished:
                return;
            default:
                timerText.text = "시간초과!";
                break;
        }

        int totalSeconds = Mathf.FloorToInt(remainingTime);
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        timerText.text = $"{minutes:00}:{seconds:00}";
    }

    // 현재 라운드 카운터 업데이트 메서드
    public void UpdateCounter()
    {
        if (!IsLocalLane)
            return;
        if (roundText == null)
            return;
        int displayRound = state == WaveState.Running ? Mathf.Max(1, currentWaveIndex) : Mathf.Min(currentWaveIndex + 1, waveList.Count);
        roundText.text = $"{displayRound} / {waveList.Count}";
    }

    void UpdateSlider()
    {
        if (timeslider == null)
            return;
        switch (state)
        {
            case WaveState.Running:
                float remainingTime = Mathf.Clamp(WaveLimitTimer - currentWaveTimer, 0f, WaveLimitTimer);
                timeslider.maxValue = WaveLimitTimer;
                timeslider.value = remainingTime;
                break;
            case WaveState.Waiting:
                float remainingRestTime = Mathf.Clamp(waveStartTimer - waveRestTimer, 0f, waveStartTimer);
                timeslider.maxValue = waveStartTimer;
                timeslider.value = remainingRestTime;
                break;
            case WaveState.Finished:
                timeslider.value = 0f;
                break;
        }
    }

    void UpdateDeathCountUI()
    {
        if (deathCountText == null)
            return;
        deathCountText.text = $"{activeEnemyCount} / {maxDeathCount}";
    }
}
