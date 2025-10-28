using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum WaveState
{
    Running,
    Waiting
}

/* ---  제이슨 파일데이터들을 받아올 클래스 --- */
[System.Serializable]
public class WaveData
{
    public int HP;
    public int KillGold;
}
public class WaveStatusWrapper
{
    public List<WaveData> EnemyStatus;
}
/*-------------------------------*/

public class WaveManager : MonoBehaviour
{
    // 웨이브 매니저 클래스
    [SerializeField] private int ownerIndex;                // 웨이브 매니저 소유자 인덱스 (플레이어 인덱스)
    [SerializeField] private EnemySpawner spawner;          // 매니저와 연결 할 스포너
    [SerializeField] private List<Wave> waveList;           // 웨이브 소환 리스트
    [SerializeField] private List<WaveData> _waves;         // 웨이브

    private int currentWaveIndex = 0;                       // 현재 웨이브 값

    /* -----  타이머 관련 ------ */
    private float waveRestTimer = 0;                                        // 대기시간 용 타이머
    public float currentWaveTimer = 0;                                      // 현재 웨이브 타이머
    private float WaveLimitTimer = 120f;                                    // 웨이브 제한 시간 타이머
    private float waveStartTimer = 5f;                                      // 웨이브 시작 시간

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI roundText;                     // UI 웨이브 카운터 텍스트
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private Slider timeslider;
    [SerializeField] private TextMeshProUGUI deathCountText;

    [Header("한계 데스카운트")]
    public int maxDeathCount;                                   // 최대 DeathCount값

    // 이번 라운드 스폰 등록 완료 플래그
    private bool enqueuedAll = false;

    /* ------- 데이터(Json)파일 -------*/
    public TextAsset jsonFile;
    
    
    // UI에 쓸 변수
    private float remainingTime;
    private int deathCount = 0;
    


    WaveState state = WaveState.Waiting;                        // 웨이브 열거형 클래스

    private void Awake()
    {
        waveList        = Waves();
        maxDeathCount   = 80;
    }

    private void Start()
    {
        if (jsonFile != null)   // 임시 단일 기준
        {
            var wrapper = JsonUtility.FromJson<WaveStatusWrapper>(jsonFile.text);
            _waves      = wrapper.EnemyStatus;
        }
        UpdateCounter();
    }

    private void Update()
    {
        UpdateTimerUI();
        UpdateSlider();
        UpdateDeathCountUI();

        if (state == WaveState.Running)                         // 0) 현재 웨이브가 진행 중일 때
        {            
            currentWaveTimer += Time.deltaTime;                 // 1) 라운드 진행 시간

            if (enqueuedAll && spawner.spawnQueue.Count == 0)   // 2) 스폰이 모두 끝났고(등록 끝 + 남아있는 큐X) → 휴식으로 전환
            {
                state               = WaveState.Waiting;
                waveRestTimer       = 0f;
                currentWaveTimer    = 0f;
                enqueuedAll         = false;                    // 다음 라운드 대비
                UpdateCounter();
            }
            
            else if (currentWaveTimer >= WaveLimitTimer)        // 3) 시간초과
            {
                state               = WaveState.Waiting;
                waveRestTimer       = 0f;
                currentWaveTimer    = 0f;
                enqueuedAll         = false;                    // 다음 라운드로 넘어감 (겹쳐 싸우기 허용)
                UpdateCounter();
                CheckGameOver();                                // maxDeathCount 도달 시 종료
            }
        }
        else if (state == WaveState.Waiting)                    // 4) 대기 중일 때
        {
            waveRestTimer += Time.deltaTime;                    // 대기 시간 타이머 증가
            if (waveRestTimer >= waveStartTimer)                // 휴식 타이머가 끝나면 다음 라운드 시작(적이 남아있어도 무조건 진행)
            {
                StartWave();
            }
        }
    }

    // 웨이브 시작 메서드
    void StartWave()
    {
        if (currentWaveIndex >= waveList.Count) return;

        var game = GameManager.Instance;

        // HP/Gold 세팅(내 라인만)
        if (_waves != null && currentWaveIndex < _waves.Count)
        {
            double baseHp = _waves[currentWaveIndex].HP;
            spawner.enemyHp = baseHp * (double)game.difficultyEnemyBonusHealth;
            spawner.killGold = _waves[currentWaveIndex].KillGold;

            // 선택: UI 동기화용 브로드캐스트
            UDPClient.Instance?.SendUDP(
                JsonUtility.ToJson(new WaveStartMessage
                {
                    spawnerId   = spawner.spawnerId,
                    ownerIndex  = ownerIndex,
                    waveIndex   = currentWaveIndex,
                    hp          = spawner.enemyHp,
                    killGold    = spawner.killGold
                }),
                "WAVE_START");
        }

        StartCoroutine(EnqueueWave(waveList[currentWaveIndex]));
        state               = WaveState.Running;
        currentWaveTimer    = 0f;

        UpdateCounter();                                       // UI 업데이트

        currentWaveIndex++;
    }

    IEnumerator EnqueueWave(Wave wave)                              // 스폰할 웨이브를 큐에 등록하는 코루틴 
    {
        foreach (var spawn in wave.spawns)
        {
            for (int i = 0; i < spawn.count; i++)                   // wave구조체의 count 수를 
            {
                spawner.EnqueueEnemy(spawn.type);                   // 몬스터의 타입을 큐에 추가
                yield return null;
            }
        }
        enqueuedAll = true;                                         // 이 라운드 스폰 등록 완료
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
        if (wave <= 0) return null;
        if (wave % 10 == 0) return null; // 보스 라운드
        int segment = (wave - 1) / 10;         // 1~9 → 0, 11~19 → 1, …
        int idx = segment % Rotation.Length;   // 0~3 반복
        return Rotation[idx];
    }

    public List<Wave> Waves(int maxWave = 100)                      // 1~100 웨이브 리스트 생성 메서드
    {
        var waves = new List<Wave>(maxWave);                        // 최대 웨이브 수 만큼 리스트 생성

        for (int wave = 1; wave <= maxWave; wave++) 
        {
            var w = new Wave { spawns = new List<SpawnInfo>() };    // 새로운 웨이브 생성

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

    // 게임 오버 체크 메서드
    void CheckGameOver()
    {
        if (deathCount >= maxDeathCount)
        {
            UDPClient.Instance.ExitRoom_UDP();
        }
    }

    // ------------- UI관련 메서드들 -----------//

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
        if (roundText == null) return;

        int displayRound = Mathf.Min(currentWaveIndex + 1, waveList.Count);

        roundText.text = $"{displayRound} / {waveList.Count}";
    }
    void UpdateSlider()
    {
        if (timeslider == null)                                        // 임시 단일 ui
            return;
        switch(state)
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
        }
    }

    public void IncrementDeathCount()
    {
        deathCount = Mathf.Min(maxDeathCount, deathCount + 1);
        UpdateDeathCountUI();
        CheckGameOver();
    }

    public void DecrementDeathCount()
    {
        deathCount = Mathf.Max(0, deathCount - 1);
        UpdateDeathCountUI();
    }

    void UpdateDeathCountUI()
    {
        if (deathCountText == null)                                      // 임시 단일 ui
            return;
        deathCountText.text = $"{deathCount} / {maxDeathCount}";
    }
}
