using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public class Enemy : Unit , IEnemySelectable
{
    EnemySpawner spawner;                                                               // 스포너 참조
    public List<Transform> route = new List<Transform>();                               // 이동 경로 리스트
    [SerializeField] float rotLerpSpeed = 20f;                                          // 클수록 빨리 도는 비율

    private float savedSpeed;
    private bool wasMoving = false;                                                     // 이동 시작 여부
    private Vector3 LastPos;                                                            // 마지막 위치 (이동 감지용)
    private bool isInvincibility = false;                                               // 무적 상태 여부
    private float invincibleDuration = 5f;                                              // 무적 유지 시간
    private float invincibleCooldown = 5f;                                              // 무적 쿨다운

    [Header("적 UI관련")]
    [SerializeField] private Vector3 hpBarOffset = new Vector3(0f, 2.0f, 0f);           // HP바 오프셋 (적 위에 표시)
    private Vector2 statusTextOffset_TakeDamage = new Vector2(0f, 3.0f);                // 상태 텍스트 오프셋 (HP바 기준)
    private Vector2 statusTextOffset_Healing = new Vector2(1f, 4.0f);                   // 상태 텍스트 오프셋 (HP바 기준)
    private Vector2 statusTextOffset_AddGold = new Vector2(2f, 5.0f);                   // 상태 텍스트 오프셋 (HP바 기준)

    private Slider hpSliderPrefab;                                                      // HP바 오브젝트
    private RectTransform hpUI;                                                         // HP바 UI 상 위치
    private Slider hpSlider;                                                            // HP바 슬라이더 컴포넌트
    private Canvas hpBarCanvas;                                                         // HP바 캔버스 (UI용)
    private RectTransform hpBarCanvasRect;                                              // HP바 캔버스의 RectTransform

    private TMP_Text statusTextPrefab;                                                  // 상태 텍스트 프리팹
    private float statusFloatDuration = 1f;                                             // 위로 이동 + 페이드 시간(초)
    private float statusFloatRise = 40f;                                                // 화면 픽셀 기준 Y 상승량

    private Animator animator;                                                          // 애니메이터 컴포넌트
    private Coroutine stunRoutine;                                                      // 스턴 루틴
    private Coroutine moveRoutine;                                                      // 이동 루틴
    private bool runPatrol => spawner != null && spawner.PatrolEnabled;                 // 스포너가 존재하고 순찰이 활성화된 경우에만 이동
    private bool _isDead;                                                               // 적이 죽었는지 여부

    [Header("보스 스킬 관련(인스펙터에서 할당)")]
    [SerializeField] private DecalProjector enemySkillProjector;                        // URP Decal Projector
    private float skillChargeDuration = 20f;                                            // 1 → 50까지 늘어나는 시간
    private float skillMaxDepth = 50f;                                                  // 목표 Projection Depth
    private float disableDuration = 5f;                                                 // 타워 침묵 시간

    private bool bossSkillRunning;                                                      // 보스 스킬 실행 중 여부
    private bool isStunned;                                                             // Stun 감지용 플래그
    private Coroutine bossSkillReplicateCo;                                             // 보스 스킬 복제용 코루틴
    private int typeArmor = 0;                                                  // 적의 타입별 추가 방어력 (예: HealthRegen, Invincible 등)

    private void Awake()
    {
        if (EnemyType == EnemyType.Boss) { animator = GetComponent<Animator>(); }
    }

    public void InitializeEnemy(int id, double hp, float add_armor, long gold, int spawnerId, EnemySpawner spawner)
    {
        this.EnemyId    = id;
        this.HP         = hp;
        this.MaxHP      = hp;
        this.Armor      = add_armor;
        this.KillGold   = gold;
        this.spawnerId  = spawnerId;
        this.spawner    = spawner;
        _isDead = false;

        CreateHpUI();       // HP UI 생성
        UpdateHpUI();       // 초기 HP UI 갱신

        if (EnemyType == EnemyType.Boss && animator)
        {
            if (Speed > 0.001f) { animator.SetTrigger("RUN"); wasMoving = true; }
            else { animator.SetTrigger("IDLE"); wasMoving = false; }
        }
    }

    private void Update()
    {
        // 보스만 애니메이션 트리거 자동 전환
        if (EnemyType == EnemyType.Boss && animator)
        {
            // “이동속도가 0이면 IDLE, 그 외엔 RUN”
            bool moving = Speed > 0.001f;

            if (moving != wasMoving)
            {
                wasMoving = moving;
                if (moving)
                {
                    animator.ResetTrigger("IDLE");
                    animator.SetTrigger("RUN");
                }
                else
                {
                    animator.ResetTrigger("RUN");
                    animator.SetTrigger("IDLE");
                }
            }
        }

        // 이동 감지용 마지막 위치 저장
        LastPos = transform.position;
    }

    public void Stun(float duration)
    {
        if (duration <= 0f) return;
        Debug.Log($"[Enemy {EnemyId}] STUN for {duration}s");

        if (stunRoutine != null) StopCoroutine(stunRoutine);
        stunRoutine = StartCoroutine(StunRoutine(duration));
    }

    private IEnumerator StunRoutine(float duration)
    {
        isStunned = true;

        // 현재 속도 저장(이미 Stun 중이어도 마지막 저장값으로 덮는 방식)
        savedSpeed = Speed;

        // 속도 0으로
        Speed = 0f;

        // 보스면 IDLE 강제
        if (EnemyType == EnemyType.Boss && animator)
        {
            animator.ResetTrigger("RUN");
            animator.SetTrigger("IDLE");
        }

        yield return new WaitForSeconds(duration);

        // 원래 속도로 복원
        Speed = savedSpeed;

        // 복원 후 보스가 다시 움직일 수 있다면 RUN
        if (EnemyType == EnemyType.Boss && animator)
        {
            if (Speed > 0.001f)
            {
                animator.ResetTrigger("IDLE");
                animator.SetTrigger("RUN");
                wasMoving = true;
            }
            else
            {
                wasMoving = false;
            }
        }
        isStunned = false;

        stunRoutine = null;
    }

    // 항상 적의 HP바가 카메라를 바라보도록 설정
    private void LateUpdate()
    {
        // 월드→스크린→캔버스 좌표 변환

        if (!hpUI || !hpBarCanvas) return;                                                                          // HP UI가 없거나 캔버스가 할당되지 않은 경우 함수 탈출

        var cam = Camera.main;                                                                                      // 메인 카메라 참조
        if (!cam) return;

        Vector3 world = transform.position + hpBarOffset;                                                           // 적 위치 + 오프셋 (월드 좌표)
        Vector3 screen = cam.WorldToScreenPoint(world);                                                             // 월드 좌표를 스크린 좌표로 변환

        if (screen.z <= 0f)                                                                                         // 스크린 좌표가 카메라 뒤에 있는 경우 비활성화
        {
            hpUI.gameObject.SetActive(false);
            return;
        }

        hpUI.gameObject.SetActive(true);                                                                            // 스크린 좌표가 카메라 앞에 있으면 활성화

        // Overlay 캔버스는 camera=null을 넘겨야 함
        var canvasComp = hpBarCanvas.GetComponentInParent<Canvas>();                                                // 캔버스 컴포넌트 참조
        Camera rectCam = (canvasComp && canvasComp.renderMode == RenderMode.ScreenSpaceOverlay) ? null : cam;       // 캔버스 렌더 모드가 Overlay면 카메라를 null로 설정

        RectTransformUtility.ScreenPointToLocalPointInRectangle(hpBarCanvasRect, screen, rectCam, out var local);   // 스크린 좌표를 로컬 좌표로 변환
        hpUI.anchoredPosition = local;                                                                              // 로컬 좌표를 HP UI의 앵커 위치로 설정
    }

    // HP바 프리팹을 설정하는 메소드
    public void SetHpBarPrefab(Canvas canvas, Slider sliderPrefab, TMP_Text statusPrefab)
    {
        hpSliderPrefab      = sliderPrefab;
        statusTextPrefab    = statusPrefab;
        hpBarCanvas         = canvas;
        if (hpBarCanvas != null)
        {
            hpBarCanvasRect = hpBarCanvas.GetComponent<RectTransform>();
        }
        else Debug.LogWarning("[Enemy] HP바 캔버스가 할당되지 않았습니다. EnemySpawner의 hpBarCanvas를 확인하세요.");
    }

    // HP바 프리팹 인스턴스화
    private void CreateHpUI()
    {
        if (hpBarCanvas == null || hpSliderPrefab == null) return;  // 만약 캔버스, 프리팹이 할당되지 않은 경우 함수 탈출

        Slider slider   = Instantiate(hpSliderPrefab, hpBarCanvasRect);
        hpUI            = slider.GetComponent<RectTransform>();
        hpSlider        = slider;

        // 슬라이더 설정
        hpSlider.minValue       = 0f;
        hpSlider.maxValue       = 1f;
        hpSlider.wholeNumbers   = false;

        var sel = hpSlider as Selectable;
        // Selectable 컴포넌트 즉, 슬라이더에 물리적 입력으로 변화를 줄 수 있는 요소가 있을 경우 트랜지션 None으로 설정
        if (sel) sel.transition = Selectable.Transition.None;

        hpUI.localScale = Vector3.one;                              // HP바 크기 조정
        slider.gameObject.SetActive(true);                          // HP바 UI 활성화

        EnemyAbilityLoops();                                        // 적 능력 루프 시작
    }

    // HP 비율 갱신 (0~1)
    private void UpdateHpUI()
    {
        if (hpSlider == null) return;
        float percent = (float)((MaxHP > 0) ? (HP / MaxHP) : 0f);
        hpSlider.SetValueWithoutNotify(Mathf.Clamp01(percent));
    }

    // 제거(죽을 때)
    private void CleanupHpUI()
    {
        if (hpUI != null)
        {
            Destroy(hpUI.gameObject);
            hpUI = null;
            hpSlider = null;
        }
    }

    //상태 텍스트 출력: HPBarCanvas 기준, 적 현재 위치에서 생성
    private void ShowStatusText(string message, string gradient,int posNum, Vector3 worldPos)
    {
        if (statusTextPrefab == null || hpBarCanvasRect == null || hpBarCanvas == null) return;

        var cam = Camera.main;
        if (!cam) return;

        // 월드→스크린→캔버스 로컬 좌표 (HP바와 동일한 방식)
        Vector3 screen = cam.WorldToScreenPoint(worldPos);
        if (screen.z <= 0f) return;

        var canvasComp = hpBarCanvas.GetComponentInParent<Canvas>();
        Camera rectCam = (canvasComp && canvasComp.renderMode == RenderMode.ScreenSpaceOverlay) ? null : cam;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(hpBarCanvasRect, screen, rectCam, out var local);

        // 인스턴스 생성
        TMP_Text label = Instantiate(statusTextPrefab, hpBarCanvasRect);
        var rect = label.rectTransform;
        switch(posNum)
        {
            case 0: // 데미지
                rect.anchoredPosition = local + statusTextOffset_TakeDamage;
                break;
            case 1: // 회복
                rect.anchoredPosition = local + statusTextOffset_Healing;
                break;
            case 2: // 골드
                rect.anchoredPosition = local + statusTextOffset_AddGold;
                break;
        }
        label.text = $"<gradient={gradient}>{message}</gradient>";
        label.raycastTarget = false;

        // 자체 코루틴으로 애니메이션(Enemy.StopAllCoroutines의 영향 X)
        var runner = label.gameObject.AddComponent<FloatingText>();
        runner.Run(rect, label, statusFloatDuration, statusFloatRise);
    }

    // 상태 텍스트 애니메이션용 내부 클래스
    private class FloatingText : MonoBehaviour
    {
        public void Run(RectTransform rect, TMP_Text label, float duration, float rise)
        {
            StartCoroutine(Animate(rect, label, duration, rise));
        }

        private IEnumerator Animate(RectTransform rect, TMP_Text label, float duration, float rise)
        {
            float t = 0f;
            Vector2 start = rect.anchoredPosition;
            Vector2 end = start + new Vector2(0f, rise);

            // 1초 동안 위로 이동 + 알파 1→0
            while (t < duration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / duration);

                rect.anchoredPosition = Vector2.Lerp(start, end, p);

                var c = label.color;
                c.a = 1f - p;
                label.color = c;

                yield return null;
            }

            Destroy(gameObject); // label 오브젝트 파괴
        }
    }

    // 경로 설정 및 이동 시작
    public void EnemyRouteSetting(int pathOwnerIndex, EnemySpawner spawner)
    {
        this.spawner = spawner;

        route.Clear();
        var path = GameManager.Instance.GetEnemyPath(pathOwnerIndex);
        if (path == null || path.Count == 0) { Debug.LogWarning("경로 없음"); return; }
        route.AddRange(path);

        // Invoke(1f) 제거: 즉시 시작 (필요하면 메시지의 spawnTime으로 보정)
        StartMove();
    }

    private void EnemyAbilityLoops()
    {
        switch (this.EnemyType)
        {
            case EnemyType.HealthRegen:
                typeArmor = 0; // HealthRegen 타입은 고정 무시값 증가 없음
                StartCoroutine(HealthRegenLoop());
                break;
            case EnemyType.Invincible:
                typeArmor = 0; // Invincible 타입은 고정 무시값 증가 없음
                StartCoroutine(InvincibilityLoop());
                break;
            case EnemyType.MovementSpeed:
                typeArmor = 0; // MovementSpeed 타입은 고정 무시값 증가 없음, 인스펙터에서 속도 조절
                break;
            case EnemyType.Armor:
                typeArmor = 50; // Armor 타입은 고정 무시값 50%
                break;
            case EnemyType.Boss:
                typeArmor = 50; // Boss 타입은 고정 무시값 50%, 회복 및 무적 루틴
                StartCoroutine(HealthRegenLoop());
                StartCoroutine(InvincibilityLoop());
                break;
        }
    }

    private IEnumerator HealthRegenLoop()
    {
        var waiting = new WaitForSeconds(5f);
        while (!_isDead)
        {
            // 최대체력의 2% 회복만 위임
            if (HP > 0 && HP < MaxHP)
            {
                Healing(MaxHP * 0.02, isAuthoritative: true);
            }
            yield return waiting;
        }
    }

    private IEnumerator InvincibilityLoop()
    {
        var on = new WaitForSeconds(invincibleDuration);
        var off = new WaitForSeconds(invincibleCooldown);

        while (!_isDead)
        {
            if (HP <= 0) { yield return null; continue; }

            isInvincibility = true;   // 무적 ON
            yield return on;

            isInvincibility = false;  // 무적 OFF
            yield return off;         // 쿨다운
        }
    }

    // 적이 경로를 따라 이동을 시작하는 메소드
    void StartMove()
    {
        if (moveRoutine != null) StopCoroutine(moveRoutine);
        moveRoutine = StartCoroutine(FollowRoute());
    }

    // 적이 경로를 따라 이동하는 코루틴
    IEnumerator FollowRoute()
    {
        // 이동이 활성화된 경우에만 실행
        const float arriveEps = 0.1f;

        // 회전 규칙: (0,2)는 180에서 -90씩, (1,3)은 -180에서 +90씩
        bool groupA = (spawnerId % 2 == 0); // 0,2 => true / 1,3 => false
        float baseYaw = groupA ? 180f : -180f;
        float stepYaw = groupA ? -90f : +90f;

        bool started = true; // 첫 이동 시작 여부

        while (runPatrol)
        {
            for (int i = 0; i < route.Count && runPatrol; i++)
            {
                var target = route[i];
                if (target == null) continue;                

                // 웨이포인트 i로 향하기 전, 규칙에 따라 Y축 회전 세팅(최초 생성 시 회전 적용 안함)
                Quaternion targetRot = started ? transform.rotation : Quaternion.Euler(0f, baseYaw + stepYaw * (i-1), 0f);

                // 최초 1번째 웨이포인트 타깃 지정 후, 이동 전 방향고정 해제
                if (started && target == route[1]) started = false;

                // i번째 웨이포인트로 이동 + 회전 보간
                while (runPatrol && target != null && Vector3.Distance(transform.position, target.position) > arriveEps)
                {
                    transform.position = Vector3.MoveTowards(
                        transform.position, target.position, Speed * Time.deltaTime);

                    // 회전 보간 (부드럽게 Yaw로 수렴)
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotLerpSpeed * Time.deltaTime);
                    yield return null;
                }
                if (this.EnemyType == EnemyType.Boss && i == 2 && !bossSkillRunning)
                    yield return StartCoroutine(BossSkillRoutine());    // 3번째 웨이포인트 도착 시 보스 스킬 실행
            }
            // 끝까지 돌면 다시 0번부터 (runPatrol이 false면 종료)
        }
    }

    private IEnumerator BossSkillRoutine()
    {
        if (enemySkillProjector == null) yield break;
        bossSkillRunning = true;

        // 1) 멈추고 스포너 방향으로 바라보기
        float prevSpeed = Speed;
        Speed           = 0f;
        if (animator) animator.SetTrigger("IDLESHOOT");

        // 스포너 방향(Y축만)으로 보스 회전
        Transform spRef = (spawner != null) ? spawner.transform : null;
        if (spRef != null)
        {
            Vector3 dir = spRef.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        // 2) Projector 켜기
        if (enemySkillProjector)
        {
            enemySkillProjector.gameObject.SetActive(true);

            // 깊이 리셋
            var size = enemySkillProjector.size;
            size.z = 1f;
            enemySkillProjector.size = size;

            // 네트워크 통지(서버/다른 클라에 범위 시각화 시작)
            UDPClient.Instance?.SendUDP(JsonUtility.ToJson(new BossSkillMessage
            {
                spawnerId       = spawnerId,
                enemyId         = EnemyId,
                start           = true,
                pos             = transform.position,
                fwd             = transform.forward,
                growDuration    = skillChargeDuration,
                maxDepth        = skillMaxDepth
            }), "BOSS_SKILL");
        }

        // 3) ProjectionDepth(=DecalProjector.size.z) 1→50 서서히 증가(20초) or 스턴 시 중단
        float t = 0f;
        while (t < skillChargeDuration)
        {
            if (isStunned || stunRoutine != null) break;

            t += Time.deltaTime;
            if (enemySkillProjector)
            {
                var sz = enemySkillProjector.size;
                sz.z = Mathf.Min(Mathf.Lerp(1f, skillMaxDepth, t / skillChargeDuration), skillMaxDepth);
                enemySkillProjector.size = sz;

                if (sz.z >= skillMaxDepth - 0.01f) break;
            }
            yield return null;
        }

        // 4) SHOOT 트리거 & 범위 내 타워 5초간 공격불가(공격 중이면 강제 종료)
        if (animator) animator.SetTrigger("SHOOT");

        // 0.1초 대기
        yield return new WaitForSeconds(0.1f);

        // 범위 내 타워 침묵 적용
        ApplyBossSilence(enemySkillProjector, disableDuration);

        // 네트워크 통지(서버/다른 클라에 범위 시각화 종료)
        UDPClient.Instance?.SendUDP(JsonUtility.ToJson(new BossSkillMessage
        {
            spawnerId   = spawnerId,
            enemyId     = EnemyId,
            start       = false
        }), "BOSS_SKILL");

        // 5) 이펙트 종료/리셋, 다시 IDLE → 순찰 재개
        if (enemySkillProjector)
        {
            enemySkillProjector.gameObject.SetActive(false);
            var size = enemySkillProjector.size; size.z = 1f; enemySkillProjector.size = size;
        }
        if (animator) animator.SetTrigger("IDLE");
        Speed = prevSpeed;
        bossSkillRunning = false;
    }

    // 보스 스킬 범위 내 타워 침묵 적용
    private void ApplyBossSilence(DecalProjector proj, float duration)
    {
        if (proj == null) return;

        // Projector 크기를 BoxOverlap으로 환산(회전 포함)
        Vector3 half    = new Vector3(proj.size.x * 0.5f, proj.size.y * 0.5f, proj.size.z * 0.5f);
        Vector3 center  = proj.transform.position;
        Quaternion rot  = proj.transform.rotation;
        
        // 범위 내 모든 콜라이더 검색
        var cols = Physics.OverlapBox(center, half, rot, ~0, QueryTriggerInteraction.Collide);
        foreach (var c in cols)
        {
            var tc = c.GetComponentInParent<TowerController>();
            if (tc) tc.ForceStopAttackAndSilence(duration);
        }
    }

    // UDP 수신 시 호출
    public void ReplicateBossSkillStart(Vector3 pos, Vector3 fwd, float duration, float maxDepth)
    {
        if (enemySkillProjector == null) return;

        if (fwd.sqrMagnitude > 0.01f) transform.forward = fwd;

        if (bossSkillReplicateCo != null) StopCoroutine(bossSkillReplicateCo);
        bossSkillReplicateCo = StartCoroutine(Co_SkillGrow(duration, maxDepth));

        if (animator) animator.SetTrigger("IDLESHOOT");
    }

    private IEnumerator Co_SkillGrow(float duration, float maxDepth)
    {
        enemySkillProjector.gameObject.SetActive(true);

        // URP Decal Projector size.z 조절
        var size = enemySkillProjector.size;
        size.z = 1f;
        enemySkillProjector.size = size;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            size = enemySkillProjector.size;
            size.z = Mathf.Min(Mathf.Lerp(1f, maxDepth, t / duration), maxDepth);
            enemySkillProjector.size = size;
            yield return null;
        }
        bossSkillReplicateCo = null;
    }

    public void ReplicateBossSkillStopAndShoot()
    {
        if (animator) animator.SetTrigger("SHOOT");

        // 범위 내 타워 침묵 적용 5초
        ApplyBossSilence(enemySkillProjector, disableDuration);

        if (enemySkillProjector)
        {
            enemySkillProjector.gameObject.SetActive(false);
            var s = enemySkillProjector.size;
            s.z = 1f;
            enemySkillProjector.size = s;
        }

        if (animator) animator.SetTrigger("IDLE");
    }

    // HP 회복(데미지와 대칭). 권한있는 곳에서만 실제 계산/브로드캐스트
    public void Healing(double amount, bool isAuthoritative = true, bool clampToMax = true)
    {
        if (!isAuthoritative) return;
        if (_isDead || amount <= 0) return;

        double max = MaxHP;
        double prev = HP;

        // 과치유 방지
        HP = clampToMax ? Math.Min(max, HP + amount) : HP + amount;
        if (HP > max) HP = max;
        if (HP < 0) HP = 0;

        // 떠오르는 회복 텍스트
        double healed = HP - prev;
        if (healed > 0)
            ShowStatusText($"+{healed:0}", "EnemyHealing", 1, transform.position + hpBarOffset);

        UpdateHpUI(); // 슬라이더 갱신

        // 기존 ENEMY_DAMAGE 메시지 재사용(remainingHp만 동기화)
        UDPClient.Instance?.SendUDP(JsonUtility.ToJson(new EnemyDamageMessage
        {
            enemyId = this.EnemyId,
            spawnerId = this.spawnerId,
            remainingHp = this.HP
        }), "ENEMY_DAMAGE");
    }

    public void TakeDamage(float damage,bool trueDamage, bool isAuthoritative = true, int attackerIndex = -1)
    {
        // 권한 없는 클라면 로컬 계산 금지(연출만)
        if (!isAuthoritative) return;

        // 이미 죽었거나 데미지 0 이하면 무시
        if (HP <= 0 || damage <= 0) return;

        // 무적일 때 데미지 무효
        if (isInvincibility)
            return;

        // 0~1로 클램프된 방어율
        float armorPct = trueDamage ? 0f : Mathf.Clamp01(Armor / 100f);                       // 난이도별 방어력 비율 (0~1), 관통일 경우 0f
        float armorPctWithType = trueDamage ? 0f : Mathf.Clamp01(typeArmor / 100f);           // 타입별 방어력 비율 (0~1),   관통일 경우 0f

        // 기본 감산: damage * (1 - Armor%) * (1 - TypeArmor%)
        double applied = damage * (1.0 - armorPct) * (1.0 - armorPctWithType);

        // 최소 피해 보장: '방어력 100%'일 때만 1 보장
        // (Armor가 100 미만이면 그대로 비율 피해, 아주 작은 DoT도 그대로 허용)
        if (armorPct >= 1.0f && damage >= 1f && applied < 1.0)
            applied = 1.0;

        // 떠오르는 데미지 텍스트 (최종 적용량)
        if (applied > 0.0)
            ShowStatusText($"-{applied:0}", "EnemyTakeDamage", 0, transform.position + hpBarOffset);
        else if(applied == 0.0)
            ShowStatusText($"무적!!", "EnemyTakeDamage", 0, transform.position + hpBarOffset);

        HP = Math.Max(0, HP - applied);
        UpdateHpUI();

        // 데미지 브로드캐스트
        UDPClient.Instance?.SendUDP(JsonUtility.ToJson(new EnemyDamageMessage
        {
            enemyId     = this.EnemyId,
            spawnerId   = this.spawnerId,
            remainingHp = this.HP
        }), "ENEMY_DAMAGE");

        if (HP <= 0 && !_isDead)
        {
            _isDead = true; // 중복 죽음 방지

            // 죽음 브로드캐스트
            UDPClient.Instance?.SendUDP(JsonUtility.ToJson(new EnemyDeathMessage
            {
                enemyId     = this.EnemyId,
                spawnerId   = this.spawnerId,
            }), "ENEMY_DEATH");
            Die(true);
        }
    }

    // 네트워크 수신용: 재계산 금지, 값만 세팅
    public void ApplyDamageNetwork(double newHp)
    {
        if (_isDead) return;
        HP = newHp;
        UpdateHpUI();
        Debug.Log($"[Enemy {EnemyId}] {HP}/{MaxHP}");
        if (HP <= 0)
        {
            _isDead = true;
            Die(false);
        }
    }

    public void Die(bool awardGold = true)
    {
        if (_isDead) { /* 중복 보호 */ } else { _isDead = true; }
        // 선택 UI 닫기
        if (UIManager.Instance != null && UIManager.Instance.GetCurrentEnemy() == this)
            UIManager.Instance.ShutdownEnemyInfoUI();

        // 보상
        if (awardGold && spawner?.player != null) 
        {
            spawner.player.AddGold(KillGold);
            ShowStatusText($"+{KillGold:0}", "AddGold", 2, transform.position + hpBarOffset);
        }

        spawner?.OnEnemyDiedLocal();

        // 스포너 정리
        if (spawner != null)
            spawner.enemies.Remove(this);

        if (EnemyType == EnemyType.Boss && animator)
            animator.SetTrigger("DIE");

        // HP바 UI / 코루틴 / 오브젝트 정리
        CleanupHpUI();
        StopAllCoroutines();
        Destroy(gameObject);
    }

    // 적이 비활성화될 때 코루틴 중지
    private void OnDisable()
    {
        StopAllCoroutines();
    }

    // IEnemySelectable 인터페이스 구현
    public void OnSelected()
    {
        UIManager.Instance.ShowEnemyInfoUI(this);
    }

    public void OnDeSelected()
    {
        UIManager.Instance.ShutdownEnemyInfoUI();
    }
}
