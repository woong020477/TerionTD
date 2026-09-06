using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>호스트의 적 시뮬레이션과 비호스트의 상태 재현을 연결합니다. HP 확정과 시각 연출을 구분합니다.</summary>
public class Enemy : Unit, IEnemySelectable
{
    EnemySpawner spawner; // 스포너 참조
    public List<Transform> route = new List<Transform>(); // 이동 경로 리스트
    [SerializeField]
    float rotLerpSpeed = 20f; // 클수록 빨리 도는 비율
    private float savedSpeed;
    private bool wasMoving = false; // 이동 시작 여부
    private Vector3 LastPos; // 마지막 위치 (이동 감지용)
    private bool isInvincibility = false; // 무적 상태 여부
    private float invincibleDuration = 5f; // 무적 유지 시간
    private float invincibleCooldown = 5f; // 무적 쿨다운
    [Header("적 UI관련")]
    [SerializeField]
    private Vector3 hpBarOffset = new Vector3(0f, 2.0f, 0f); // HP바 오프셋 (적 위에 표시)
    private Vector2 statusTextOffset_TakeDamage = new Vector2(0f, 3.0f); // 상태 텍스트 오프셋 (HP바 기준)
    private Vector2 statusTextOffset_Healing = new Vector2(1f, 4.0f); // 상태 텍스트 오프셋 (HP바 기준)
    private Vector2 statusTextOffset_AddGold = new Vector2(2f, 5.0f); // 상태 텍스트 오프셋 (HP바 기준)
    private Slider hpSliderPrefab; // HP바 오브젝트
    private RectTransform hpUI; // HP바 UI 상 위치
    private Slider hpSlider; // HP바 슬라이더 컴포넌트
    private Canvas hpBarCanvas; // HP바 캔버스 (UI용)
    private RectTransform hpBarCanvasRect; // HP바 캔버스의 RectTransform
    private TMP_Text statusTextPrefab; // 상태 텍스트 프리팹
    private float statusFloatDuration = 1f; // 위로 이동 + 페이드 시간(초)
    private float statusFloatRise = 40f; // 화면 픽셀 기준 Y 상승량
    private Animator animator; // 애니메이터 컴포넌트
    private Coroutine stunRoutine; // 스턴 루틴
    private Coroutine moveRoutine; // 이동 루틴
    private bool runPatrol => spawner != null && spawner.PatrolEnabled;

    private bool _isDead; // 적이 죽었는지 여부
    private bool _removalStarted; // 사망/도착 패킷 중복 정리 방지
    private int lastAttackerIndex = -1; // 마지막으로 실제 피해를 준 플레이어
    [Header("보스 스킬 관련(인스펙터에서 할당)")]
    [SerializeField]
    private DecalProjector enemySkillProjector; // URP Decal Projector
    private float skillChargeDuration = 20f; // 1 → 50까지 늘어나는 시간
    private float skillMaxDepth = 50f; // 목표 Projection Depth
    private float disableDuration = 5f; // 타워 침묵 시간
    private bool bossSkillRunning; // 보스 스킬 실행 중 여부
    private float bossSkillMoveSpeed; // 스킬/스턴이 겹쳐도 복구할 원래 이동 속도
    private bool isStunned; // Stun 감지용 플래그
    private Coroutine bossSkillReplicateCo; // 보스 스킬 복제용 코루틴
    private int typeArmor = 0; // 적의 타입별 추가 방어력 (예: HealthRegen, Invincible 등)
    private const float RemotePositionLerpSpeed = 15f;
    private const float RemoteRotationLerpSpeed = 18f;
    private const float MaxRemoteExtrapolationTime = 0.3f;
    private Vector3 networkTargetPosition;
    private Quaternion networkTargetRotation;
    private float lastNetworkStateReceivedAt;
    private bool hasNetworkState;
    private int currentRouteIndex;
    public bool IsAuthoritative { get; private set; }
    public bool IsAlive => !_isDead && !_removalStarted && HP > 0 && isActiveAndEnabled;

    public event Action TargetUnavailable;
    private float hpAnimationFrom;
    private float hpAnimationTarget;
    private float hpAnimationStarted;
    private float hpAnimationDuration;
    private static readonly int RunTrigger = Animator.StringToHash("RUN");
    private static readonly int IdleTrigger = Animator.StringToHash("IDLE");
    private static readonly int IdleShootTrigger = Animator.StringToHash("IDLESHOOT");
    private static readonly int ShootTrigger = Animator.StringToHash("SHOOT");
    private static readonly int DieTrigger = Animator.StringToHash("DIE");
    private void Awake()
    {
        if (EnemyType == EnemyType.Boss)
        {
            animator = GetComponent<Animator>();
        }
    }

    /// <summary>스폰 식별자와 능력치를 설정하고 호스트 여부에 따라 시뮬레이션 실행을 구분합니다.</summary>
    public void InitializeEnemy(int id, double hp, float add_armor, long gold, int spawnerId, EnemySpawner spawner, bool isAuthoritative)
    {
        this.EnemyId = id;
        this.HP = hp;
        this.MaxHP = hp;
        this.Armor = add_armor;
        this.KillGold = gold;
        this.spawnerId = spawnerId;
        this.spawner = spawner;
        IsAuthoritative = isAuthoritative;
        _isDead = false;
        _removalStarted = false;
        lastAttackerIndex = -1;
        networkTargetPosition = transform.position;
        networkTargetRotation = transform.rotation;
        lastNetworkStateReceivedAt = Time.unscaledTime;
        hasNetworkState = false;
        currentRouteIndex = 0;
        CreateHpUI();
        UpdateHpUI();
        // 적 규칙은 호스트에서만 실행하고 비호스트는 네트워크 결과만 표현한다.
        if (IsAuthoritative)
            EnemyAbilityLoops();
        if (EnemyType == EnemyType.Boss && animator)
        {
            UpdateBossLocomotion(true);
        }
    }

    private void Update()
    {
        if (!IsAuthoritative && hasNetworkState)
        {
            float snapshotAge = Mathf.Clamp(Time.unscaledTime - lastNetworkStateReceivedAt, 0f, MaxRemoteExtrapolationTime);
            Vector3 predictedPosition = networkTargetPosition;
            if (Speed > 0.001f)
                predictedPosition += networkTargetRotation * Vector3.forward * (Speed * snapshotAge * Time.timeScale);
            // 네트워크 수신 간격 사이에는 마지막 진행 방향으로 짧게 예측하고,
            // 보간 반응 속도는 게임 배속과 무관하게 일정하게 유지합니다.
            float positionT = 1f - Mathf.Exp(-RemotePositionLerpSpeed * Time.unscaledDeltaTime);
            float rotationT = 1f - Mathf.Exp(-RemoteRotationLerpSpeed * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, predictedPosition, positionT);
            transform.rotation = Quaternion.Slerp(transform.rotation, networkTargetRotation, rotationT);
        }

        // 보스만 애니메이션 트리거 자동 전환
        if (!_isDead && EnemyType == EnemyType.Boss && animator && !bossSkillRunning)
            UpdateBossLocomotion(false);
        // 이동 감지용 마지막 위치 저장
        LastPos = transform.position;
    }

    public void Stun(float duration)
    {
        if (duration <= 0f)
            return;
        if (!IsAuthoritative)
        {
            UDPClient.Instance?.SendUDP(JsonUtility.ToJson(new EnemyStunRequestMessage { enemyId = EnemyId, spawnerId = spawnerId, duration = duration }), "ENEMY_STUN_REQUEST");
            return;
        }

        if (!isStunned)
        {
            savedSpeed = bossSkillRunning ? bossSkillMoveSpeed : Speed;
            isStunned = true;
        }

        Speed = 0f;
        if (stunRoutine != null)
            StopCoroutine(stunRoutine);
        stunRoutine = StartCoroutine(StunRoutine(duration));
    }

    private IEnumerator StunRoutine(float duration)
    {
        // 보스면 IDLE 강제
        if (EnemyType == EnemyType.Boss && animator)
        {
            SetBossTrigger(IdleTrigger);
        }

        yield return new WaitForSeconds(duration);
        // 원래 속도로 복원
        Speed = savedSpeed;
        isStunned = false;
        // 복원 후 보스가 다시 움직일 수 있다면 RUN
        if (EnemyType == EnemyType.Boss && animator)
        {
            if (Speed > 0.001f)
            {
                SetBossTrigger(RunTrigger);
                wasMoving = true;
            }
            else
            {
                wasMoving = false;
            }
        }

        stunRoutine = null;
    }

    // 항상 적의 HP바가 카메라를 바라보도록 설정
    private void LateUpdate()
    {
        // 실제 HP는 즉시 확정하고, 연사 동안 보이는 게이지에만 보간을 적용합니다.
        if (hpSlider != null && hpAnimationDuration > 0f)
        {
            float progress = Mathf.Clamp01((Time.time - hpAnimationStarted) / hpAnimationDuration);
            hpSlider.SetValueWithoutNotify(Mathf.Lerp(hpAnimationFrom, hpAnimationTarget, progress));
            if (progress >= 1f)
                hpAnimationDuration = 0f;
        }

        // 월드→스크린→캔버스 좌표 변환
        if (!hpUI || !hpBarCanvas)
            return;
        var cam = Camera.main;
        if (!cam)
            return;
        Vector3 world = transform.position + hpBarOffset;
        Vector3 screen = cam.WorldToScreenPoint(world);
        if (screen.z <= 0f)
        {
            hpUI.gameObject.SetActive(false);
            return;
        }

        hpUI.gameObject.SetActive(true);
        // Overlay 캔버스는 camera=null을 넘겨야 함
        var canvasComp = hpBarCanvas.GetComponentInParent<Canvas>();
        Camera rectCam = (canvasComp && canvasComp.renderMode == RenderMode.ScreenSpaceOverlay) ? null : cam;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(hpBarCanvasRect, screen, rectCam, out var local);
        hpUI.anchoredPosition = local;
    }

    // HP바 프리팹을 설정하는 메소드
    public void SetHpBarPrefab(Canvas canvas, Slider sliderPrefab, TMP_Text statusPrefab)
    {
        hpSliderPrefab = sliderPrefab;
        statusTextPrefab = statusPrefab;
        hpBarCanvas = canvas;
        if (hpBarCanvas != null)
        {
            hpBarCanvasRect = hpBarCanvas.GetComponent<RectTransform>();
        }
        else
            Debug.LogWarning("[Enemy] HP바 캔버스가 할당되지 않았습니다. EnemySpawner의 hpBarCanvas를 확인하세요.");
    }

    // HP바 프리팹 인스턴스화
    private void CreateHpUI()
    {
        if (hpBarCanvas == null || hpSliderPrefab == null)
            return;
        Slider slider = Instantiate(hpSliderPrefab, hpBarCanvasRect);
        hpUI = slider.GetComponent<RectTransform>();
        hpSlider = slider;
        // 슬라이더 설정
        hpSlider.minValue = 0f;
        hpSlider.maxValue = 1f;
        hpSlider.wholeNumbers = false;
        var sel = hpSlider as Selectable;
        // Selectable 컴포넌트 즉, 슬라이더에 물리적 입력으로 변화를 줄 수 있는 요소가 있을 경우 트랜지션 None으로 설정
        if (sel)
            sel.transition = Selectable.Transition.None;
        hpUI.localScale = Vector3.one;
        slider.gameObject.SetActive(true);
    }

    // HP 비율 갱신 (0~1)
    private void UpdateHpUI(float presentationDuration = 0f)
    {
        if (hpSlider == null)
            return;
        hpAnimationTarget = Mathf.Clamp01((float)((MaxHP > 0) ? (HP / MaxHP) : 0f));
        if (presentationDuration > 0f)
        {
            hpAnimationFrom = hpSlider.value;
            hpAnimationStarted = Time.time;
            hpAnimationDuration = Mathf.Clamp(presentationDuration, 0f, 1f);
        }
        // 주기적인 위치/HP 스냅샷이 연사 게이지의 보간을 매번 끊지 않도록 합니다.
        else if (hpAnimationDuration <= 0f)
            hpSlider.SetValueWithoutNotify(hpAnimationTarget);
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
    private void ShowStatusText(string message, string gradient, int posNum, Vector3 worldPos)
    {
        if (statusTextPrefab == null || hpBarCanvasRect == null || hpBarCanvas == null)
            return;
        var cam = Camera.main;
        if (!cam)
            return;
        // 월드→스크린→캔버스 로컬 좌표 (HP바와 동일한 방식)
        Vector3 screen = cam.WorldToScreenPoint(worldPos);
        if (screen.z <= 0f)
            return;
        var canvasComp = hpBarCanvas.GetComponentInParent<Canvas>();
        Camera rectCam = (canvasComp && canvasComp.renderMode == RenderMode.ScreenSpaceOverlay) ? null : cam;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(hpBarCanvasRect, screen, rectCam, out var local);
        // 인스턴스 생성
        TMP_Text label = Instantiate(statusTextPrefab, hpBarCanvasRect);
        var rect = label.rectTransform;
        switch (posNum)
        {
            case 0:
                rect.anchoredPosition = local + statusTextOffset_TakeDamage;
                break;
            case 1:
                rect.anchoredPosition = local + statusTextOffset_Healing;
                break;
            case 2:
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

            Destroy(gameObject);
        }
    }

    // 경로 설정 및 이동 시작
    public void EnemyRouteSetting(int pathOwnerIndex, EnemySpawner spawner)
    {
        this.spawner = spawner;
        route.Clear();
        var path = GameManager.Instance.GetEnemyPath(pathOwnerIndex);
        if (path == null || path.Count == 0)
        {
            Debug.LogWarning("경로 없음");
            return;
        }

        route.AddRange(path);
        currentRouteIndex = 0;
        // Invoke(1f) 제거: 즉시 시작 (필요하면 메시지의 spawnTime으로 보정)
        if (IsAuthoritative)
            StartMove();
    }

    private void EnemyAbilityLoops()
    {
        if (!IsAuthoritative)
            return;
        switch (this.EnemyType)
        {
            case EnemyType.HealthRegen:
                typeArmor = 0;
                StartCoroutine(HealthRegenLoop());
                break;
            case EnemyType.Invincible:
                typeArmor = 0;
                StartCoroutine(InvincibilityLoop());
                break;
            case EnemyType.MovementSpeed:
                typeArmor = 0;
                break;
            case EnemyType.Armor:
                typeArmor = 50;
                break;
            case EnemyType.Boss:
                typeArmor = 50;
                StartCoroutine(HealthRegenLoop());
                StartCoroutine(InvincibilityLoop());
                break;
        }
    }

    private IEnumerator HealthRegenLoop()
    {
        if (!IsAuthoritative)
            yield break;
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
        if (!IsAuthoritative)
            yield break;
        var on = new WaitForSeconds(invincibleDuration);
        var off = new WaitForSeconds(invincibleCooldown);
        while (!_isDead)
        {
            if (HP <= 0)
            {
                yield return null;
                continue;
            }

            isInvincibility = true;
            yield return on;
            isInvincibility = false;
            yield return off;
        }
    }

    // 적이 경로를 따라 이동을 시작하는 메소드
    void StartMove()
    {
        if (!IsAuthoritative)
            return;
        if (moveRoutine != null)
            StopCoroutine(moveRoutine);
        moveRoutine = StartCoroutine(FollowRoute());
    }

    // 적이 경로를 따라 이동하는 코루틴
    IEnumerator FollowRoute()
    {
        // 이동이 활성화된 경우에만 실행
        const float arriveEps = 0.1f;
        if (route == null || route.Count == 0)
            yield break;
        while (runPatrol && !_isDead)
        {
            if (currentRouteIndex >= route.Count)
                currentRouteIndex = 0;
            var target = route[currentRouteIndex];
            if (target == null)
            {
                currentRouteIndex++;
                continue;
            }

            while (runPatrol && target != null && Vector3.Distance(transform.position, target.position) > arriveEps)
            {
                Vector3 direction = target.position - transform.position;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                    float rotationT = 1f - Mathf.Exp(-rotLerpSpeed * Time.deltaTime);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationT);
                }

                transform.position = Vector3.MoveTowards(transform.position, target.position, Speed * Time.deltaTime);
                yield return null;
            }

            if (EnemyType == EnemyType.Boss && currentRouteIndex == 2 && !bossSkillRunning)
                yield return StartCoroutine(BossSkillRoutine());
            currentRouteIndex++;
        }
    }

    private IEnumerator BossSkillRoutine()
    {
        if (!IsAuthoritative)
            yield break;
        if (enemySkillProjector == null)
            yield break;
        bossSkillRunning = true;
        // 1) 멈추고 스포너 방향으로 바라보기
        float prevSpeed = Speed;
        bossSkillMoveSpeed = prevSpeed;
        Speed = 0f;
        if (animator)
            SetBossTrigger(IdleShootTrigger);
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
            UDPClient.Instance?.SendUDP(JsonUtility.ToJson(new BossSkillMessage { spawnerId = spawnerId, enemyId = EnemyId, start = true, pos = transform.position, fwd = transform.forward, growDuration = skillChargeDuration, maxDepth = skillMaxDepth }), "BOSS_SKILL");
        }

        // 3) ProjectionDepth(=DecalProjector.size.z) 1→50 서서히 증가(20초) or 스턴 시 중단
        float t = 0f;
        bool interruptedByStun = false;
        while (t < skillChargeDuration)
        {
            if (isStunned || stunRoutine != null)
            {
                interruptedByStun = true;
                break;
            }

            t += Time.deltaTime;
            if (enemySkillProjector)
            {
                var sz = enemySkillProjector.size;
                sz.z = Mathf.Min(Mathf.Lerp(1f, skillMaxDepth, t / skillChargeDuration), skillMaxDepth);
                enemySkillProjector.size = sz;
                if (sz.z >= skillMaxDepth - 0.01f)
                    break;
            }

            yield return null;
        }

        if (interruptedByStun)
        {
            UDPClient.Instance?.SendUDP(JsonUtility.ToJson(new BossSkillMessage { spawnerId = spawnerId, enemyId = EnemyId, start = false, cancelled = true }), "BOSS_SKILL");
            ResetBossSkillPresentation();
            bossSkillRunning = false;
            yield break;
        }

        // 4) SHOOT 트리거 & 범위 내 타워 5초간 공격불가(공격 중이면 강제 종료)
        if (animator)
            SetBossTrigger(ShootTrigger);
        // 0.1초 대기
        yield return new WaitForSeconds(0.1f);
        // 범위 내 타워 침묵 적용
        ApplyBossSilence(enemySkillProjector, disableDuration);
        // 네트워크 통지(서버/다른 클라에 범위 시각화 종료)
        UDPClient.Instance?.SendUDP(JsonUtility.ToJson(new BossSkillMessage { spawnerId = spawnerId, enemyId = EnemyId, start = false, cancelled = false }), "BOSS_SKILL");
        // 5) 이펙트 종료/리셋, 다시 IDLE → 순찰 재개
        if (!isStunned)
            Speed = prevSpeed;
        bossSkillRunning = false;
        ResetBossSkillPresentation();
        UpdateBossLocomotion(true);
    }

    private void ResetBossSkillPresentation()
    {
        if (enemySkillProjector)
        {
            enemySkillProjector.gameObject.SetActive(false);
            var size = enemySkillProjector.size;
            size.z = 1f;
            enemySkillProjector.size = size;
        }
    }

    // 보스 스킬 범위 내 타워 침묵 적용
    private void ApplyBossSilence(DecalProjector proj, float duration)
    {
        if (proj == null)
            return;
        // Projector 크기를 BoxOverlap으로 환산(회전 포함)
        Vector3 half = new Vector3(proj.size.x * 0.5f, proj.size.y * 0.5f, proj.size.z * 0.5f);
        Vector3 center = proj.transform.position;
        Quaternion rot = proj.transform.rotation;
        // 범위 내 모든 콜라이더 검색
        var cols = Physics.OverlapBox(center, half, rot, ~0, QueryTriggerInteraction.Collide);
        foreach (var c in cols)
        {
            var tc = c.GetComponentInParent<TowerController>();
            if (tc)
                tc.ForceStopAttackAndSilence(duration);
        }
    }

    // UDP 수신 시 호출
    public void ReplicateBossSkillStart(Vector3 pos, Vector3 fwd, float duration, float maxDepth)
    {
        if (enemySkillProjector == null)
            return;
        if (fwd.sqrMagnitude > 0.01f)
            transform.forward = fwd;
        if (bossSkillReplicateCo != null)
            StopCoroutine(bossSkillReplicateCo);
        bossSkillReplicateCo = StartCoroutine(Co_SkillGrow(duration, maxDepth));
        bossSkillRunning = true;
        if (animator)
            SetBossTrigger(IdleShootTrigger);
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

    public void ReplicateBossSkillStop(bool cancelled)
    {
        if (!cancelled)
        {
            if (animator)
                SetBossTrigger(ShootTrigger);
            // 취소되지 않은 호스트 확정 결과에서만 타워 침묵을 표현한다.
            ApplyBossSilence(enemySkillProjector, disableDuration);
        }

        ResetBossSkillPresentation();
        bossSkillRunning = false;
        UpdateBossLocomotion(true);
    }

    private void UpdateBossLocomotion(bool force)
    {
        if (!animator || bossSkillRunning || _isDead)
            return;
        bool moving = Speed > 0.001f && !isStunned;
        if (!force && moving == wasMoving)
            return;
        wasMoving = moving;
        SetBossTrigger(moving ? RunTrigger : IdleTrigger);
    }

    private void SetBossTrigger(int trigger)
    {
        if (!animator)
            return;
        animator.ResetTrigger(RunTrigger);
        animator.ResetTrigger(IdleTrigger);
        animator.ResetTrigger(IdleShootTrigger);
        animator.ResetTrigger(ShootTrigger);
        animator.ResetTrigger(DieTrigger);
        animator.SetTrigger(trigger);
    }

    // HP 회복(데미지와 대칭). 권한있는 곳에서만 실제 계산/브로드캐스트
    public void Healing(double amount, bool isAuthoritative = true, bool clampToMax = true)
    {
        if (!isAuthoritative || !IsAuthoritative)
            return;
        if (_isDead || amount <= 0)
            return;
        double max = MaxHP;
        double prev = HP;
        // 과치유 방지
        HP = clampToMax ? Math.Min(max, HP + amount) : HP + amount;
        if (HP > max)
            HP = max;
        if (HP < 0)
            HP = 0;
        // 떠오르는 회복 텍스트
        double healed = HP - prev;
        if (healed > 0)
            ShowStatusText($"+{healed:0}", "EnemyHealing", 1, transform.position + hpBarOffset);
        UpdateHpUI();
        // 기존 ENEMY_DAMAGE 메시지 재사용(remainingHp만 동기화)
        UDPClient.Instance?.SendUDP(JsonUtility.ToJson(new EnemyDamageMessage { enemyId = this.EnemyId, spawnerId = this.spawnerId, remainingHp = this.HP }), "ENEMY_DAMAGE");
    }

    /// <summary>비호스트는 피격을 요청하고 호스트만 방어력·스턴·사망을 판정해 결과를 전송합니다.</summary>
    public void TakeDamage(float damage, bool trueDamage, bool isAuthoritative = true, int attackerIndex = -1, float stunChance = 0f, float stunDuration = 0f, float presentationDuration = 0f)
    {
        // 권한 없는 클라면 로컬 계산 금지(연출만)
        if (!isAuthoritative || !IsAlive || damage <= 0f || float.IsNaN(damage) || float.IsInfinity(damage))
            return;
        presentationDuration = float.IsNaN(presentationDuration) ? 0f : Mathf.Clamp(presentationDuration, 0f, 1f);
        if (!IsAuthoritative)
        {
            if (damage <= 0f || float.IsNaN(damage) || float.IsInfinity(damage))
                return;
            UDPClient.Instance?.SendUDP(JsonUtility.ToJson(new EnemyHitRequestMessage { enemyId = EnemyId, spawnerId = spawnerId, damage = damage, trueDamage = trueDamage, attackerIndex = attackerIndex, stunChance = stunChance, stunDuration = stunDuration, presentationDuration = presentationDuration }), "ENEMY_HIT_REQUEST");
            return;
        }

        // 이미 죽었거나 데미지 0 이하면 무시
        if (HP <= 0 || damage <= 0)
            return;
        // 무적일 때 데미지 무효
        if (isInvincibility)
            return;
        // 0~1로 클램프된 방어율
        float armorPct = trueDamage ? 0f : Mathf.Clamp01(Armor / 100f);
        float armorPctWithType = trueDamage ? 0f : Mathf.Clamp01(typeArmor / 100f);
        // 기본 감산: damage * (1 - Armor%) * (1 - TypeArmor%)
        double applied = damage * (1.0 - armorPct) * (1.0 - armorPctWithType);
        // 최소 피해 보장: '방어력 100%'일 때만 1 보장
        // (Armor가 100 미만이면 그대로 비율 피해, 아주 작은 DoT도 그대로 허용)
        if (armorPct >= 1.0f && damage >= 1f && applied < 1.0)
            applied = 1.0;
        // 떠오르는 데미지 텍스트 (최종 적용량)
        if (applied > 0.0)
            ShowStatusText($"-{applied:0}", "EnemyTakeDamage", 0, transform.position + hpBarOffset);
        else if (applied == 0.0)
            ShowStatusText($"무적!!", "EnemyTakeDamage", 0, transform.position + hpBarOffset);
        HP = Math.Max(0, HP - applied);
        UpdateHpUI(presentationDuration);
        if (applied > 0.0 && attackerIndex >= 0 && GameManager.Instance != null && attackerIndex < GameManager.Instance.players.Count)
            lastAttackerIndex = attackerIndex;
        if (HP <= 0 && !_isDead)
        {
            _isDead = true; // 중복 죽음 방지
            // 죽음 브로드캐스트
            UDPClient.Instance?.SendUDP(JsonUtility.ToJson(new EnemyDeathMessage { enemyId = this.EnemyId, spawnerId = this.spawnerId, killerPlayerIndex = lastAttackerIndex }), "ENEMY_DEATH");
            AwardKillGold(lastAttackerIndex);
            Die();
            return;
        }

        if (stunChance > 0f && stunDuration > 0f && UnityEngine.Random.value < Mathf.Clamp01(stunChance))
            Stun(Mathf.Min(stunDuration, 5f));
        // 치명타가 아닌 HP 변경만 별도 동기화한다. 사망은 ENEMY_DEATH 한 패킷으로 처리한다.
        UDPClient.Instance?.SendUDP(JsonUtility.ToJson(new EnemyDamageMessage { enemyId = this.EnemyId, spawnerId = this.spawnerId, remainingHp = this.HP, presentationDuration = presentationDuration }), "ENEMY_DAMAGE");
    }

    /// <summary>호스트가 확정한 HP를 반영합니다. 게이지 보간은 표현일 뿐 피해를 재계산하지 않습니다.</summary>
    public void ApplyDamageNetwork(double newHp, float presentationDuration = 0f)
    {
        if (_isDead)
            return;
        double previousHp = HP;
        HP = Math.Max(0, Math.Min(MaxHP, newHp));
        // 호스트가 확정한 HP 차이를 이용해 모든 참가자가 같은 피해/회복 피드백을 본다.
        double hpDelta = HP - previousHp;
        if (hpDelta < 0)
            ShowStatusText($"-{Math.Abs(hpDelta):0}", "EnemyTakeDamage", 0, transform.position + hpBarOffset);
        else if (hpDelta > 0)
            ShowStatusText($"+{hpDelta:0}", "EnemyHealing", 1, transform.position + hpBarOffset);
        UpdateHpUI(presentationDuration);
    }

    // 비호스트의 공격 요청은 호스트에서만 최종 판정한다.
    public void ApplyHitRequest(float damage, bool trueDamage, int attackerIndex, float stunChance, float stunDuration, float presentationDuration = 0f)
    {
        if (!IsAuthoritative)
            return;
        if (damage <= 0f || float.IsNaN(damage) || float.IsInfinity(damage))
            return;
        TakeDamage(damage, trueDamage, true, attackerIndex, Mathf.Clamp01(stunChance), Mathf.Clamp(stunDuration, 0f, 5f), presentationDuration);
    }

    public void ApplyStunRequest(float duration)
    {
        if (!IsAuthoritative || duration <= 0f || float.IsNaN(duration) || float.IsInfinity(duration))
            return;
        Stun(Mathf.Min(duration, 5f));
    }

    public void ApplyDeathNetwork(int killerPlayerIndex)
    {
        if (IsAuthoritative || _removalStarted)
            return;
        ApplyDamageNetwork(0);
        AwardKillGold(killerPlayerIndex);
        Die();
    }

    private void AwardKillGold(int killerPlayerIndex)
    {
        var game = GameManager.Instance;
        if (game == null || killerPlayerIndex < 0 || killerPlayerIndex >= game.players.Count)
            return;
        PlayerController killer = game.players[killerPlayerIndex];
        if (killer == null || !killer.IsLocalPlayer || KillGold <= 0)
            return;
        killer.AddGold(KillGold);
        ShowStatusText($"+{KillGold:0}", "AddGold", 2, transform.position + hpBarOffset);
    }

    /// <summary>수신한 위치·회전·HP를 저장해 비호스트 화면의 보간 기준으로 사용합니다.</summary>
    public void ApplyNetworkState(EnemyStateMessage state)
    {
        if (IsAuthoritative || _isDead)
            return;
        networkTargetPosition = state.position;
        networkTargetRotation = state.rotation;
        lastNetworkStateReceivedAt = Time.unscaledTime;
        Speed = Mathf.Max(0f, state.speed);
        currentRouteIndex = Mathf.Max(0, state.routeIndex);
        MaxHP = Math.Max(1, state.maxHp);
        HP = Math.Max(0, Math.Min(MaxHP, state.hp));
        UpdateHpUI();
        hasNetworkState = true;
    }

    public EnemyStateMessage CreateNetworkState()
    {
        return new EnemyStateMessage
        {
            enemyId = EnemyId,
            spawnerId = spawnerId,
            position = transform.position,
            rotation = transform.rotation,
            speed = Speed,
            routeIndex = currentRouteIndex,
            enemyType = EnemyType,
            hp = HP,
            maxHp = MaxHP,
            armor = Armor,
            killGold = KillGold
        };
    }

    public void PromoteToAuthority()
    {
        if (IsAuthoritative || _isDead)
            return;
        IsAuthoritative = true;
        hasNetworkState = false;
        EnemyAbilityLoops();
        StartMove();
    }

    public void Die()
    {
        RemoveFromMatch();
    }

    /// <summary>사망 처리를 한 번만 수행하고 표적 상실 알림·레지스트리·UI를 순서대로 정리합니다.</summary>
    private void RemoveFromMatch()
    {
        if (_removalStarted)
            return;
        _removalStarted = true;
        _isDead = true;
        TargetUnavailable?.Invoke();
        // 선택 UI 닫기
        if (UIManager.Instance != null && UIManager.Instance.GetCurrentEnemy() == this)
            UIManager.Instance.ShutdownEnemyInfoUI();
        // 스포너 정리
        if (spawner != null)
        {
            spawner.enemies.Remove(this);
            spawner.UnregisterEnemy(EnemyId, this);
            if (IsAuthoritative)
                spawner.OnEnemyKilledLocal();
        }

        bool playBossDeath = EnemyType == EnemyType.Boss && animator;
        if (playBossDeath)
        {
            SetBossTrigger(DieTrigger);
            foreach (var enemyCollider in GetComponentsInChildren<Collider>())
                enemyCollider.enabled = false;
        }

        // HP바 UI / 코루틴 / 오브젝트 정리
        CleanupHpUI();
        StopAllCoroutines();
        Destroy(gameObject, playBossDeath ? 2f : 0f);
    }

    // 적이 비활성화될 때 코루틴 중지
    private void OnDisable()
    {
        TargetUnavailable?.Invoke();
        TargetUnavailable = null;
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
