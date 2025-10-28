using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 업그레이드 데이터 구조체
public struct TowerStats
{
    public float Damage;
    public float Cost;
}

// 타워 종류를 정의하는 열거형
public enum TowerType
{
    Flame,
    Laser,
    Machine,
    Multiple,
    Rocket
}

public class TowerController : MonoBehaviour, ITowerSelectable
{
    public int TowerId { get; private set; }                                // 네트워크 고유 ID
    public PlayerController Owner { get; private set; }                     // 타워 소유자 플레이어 컨트롤러
    public TowerType towerType { get; set; }                                // 타워 종류 (예: Flame, Laser, Machine, Rocket, Multiple 등)
    public static readonly Dictionary<int, TowerController> ById = new();   // 네트워크 ID로 타워를 빠르게 찾기 위한 딕셔너리

    [Header("총알 생성 지점과 이팩트 관련")]
    public Transform bulletSpawnPoint;
    [SerializeField] private ParticleSystem bulletSpawnEffect;              // 총알 발사 시 이펙트
    private ParticleSystem bulletEffectInstance;                            // 발사 이펙트 인스턴스

    private bool particleSwitch = false;                                    // 파티클 이펙트 토글 true면 켜짐, false면 꺼짐

    [Header("타워 이름")]
    public string towerName;                    // 타워 이름 (예: "Frame", "Laser", "Machine", "Rocket", "Multiple" 등)
    public string towerDisplayName;             // UI에 표시될 타워 이름

    [Header("업그레이드 단계 및 능력치")]
    public int upgradeLevel = 1;                // 현재 업그레이드 단계
    public float damage = 10;                   // 공격력
    public float attackDelay = 1.0f;            // 공격 간격(초 단위)

    // 연사 후 딜레이
    private int burstCount = 0;                 // 현재 연사 발사 횟수
    private bool isBurstDelay = false;          // 3초 딜레이 상태인지 여부

    private Enemy currentEnemy;                 // 현재 바라보고 있는 적 Enemy 컴포넌트
    private TowerRangeDetect rangeDetect;       // 범위 감지 컴포넌트

    private float lastAttackTime = 0f;          // 마지막 공격 시간 기록
    private float rotationSpeed = 90f;          // 회전 속도
    private float idleRotationSpeed = 30f;      // 적이 없을 때 대기 회전 속도(도/초)

    private bool isSilenced;                    // 침묵 상태인지 여부
    private float silencedUntil;                // 침묵 상태가 끝나는 시간

    // 확률
    [Range(0f,1f)] private float trueDamageChance = 0.10f;

    // 정적 데이터
    private static UpgradeData upgradeData;     // 업그레이드 데이터
    private static bool isDataLoaded = false;   // 업그레이드 데이터가 로드되었는지 여부

    // 코루틴
    private Coroutine DotDamageRoutine;         // 파티클 데미지 루틴
    private Coroutine burstRoutine;             // 연사 공격 루틴

    private Transform netAimTarget;     // 네트워크로 받은 최근 타겟
    private float netAimExpireTime = 0; // 타겟 유지 만료 시각 (Time.time)

    public static List<TowerController> Towers = new List<TowerController>();           // 모든 타워를 저장하는 리스트

    public void InitializeNetwork(int id, PlayerController owner)
    {
        TowerId     = id;               // 타워의 네트워크 ID 설정
        Owner       = owner;              // 플레이어 소유자 설정
        ById[id]    = this;            // 네트워크 ID로 타워를 딕셔너리에 등록
    }

    // 타워가 활성화될 때 호출되는 메소드
    private void OnEnable()
    {
        if (!Towers.Contains(this))
            Towers.Add(this);
    }

    // 타워가 비활성화될 때 호출되는 메소드
    private void OnDisable()
    {
        Towers.Remove(this);
        if (TowerId != 0) ById.Remove(TowerId);
    }

    public void SetOwner(PlayerController player) => Owner = player;

    private void Awake()
    {
        LoadUpgradeData();                                                              // 업그레이드 데이터 로드
        if (!rangeDetect) rangeDetect = GetComponentInChildren<TowerRangeDetect>();     // 범위 감지 컴포넌트가 없으면 자식에서 찾음
    }

    private void Start()
    {
        if (bulletSpawnPoint != null && bulletSpawnEffect != null)                              // 총알 발사 지점과 이펙트가 설정되어 있다면
        {
            bulletEffectInstance = Instantiate(
                bulletSpawnEffect, bulletSpawnPoint.position, Quaternion.identity, transform);  // 총알 발사 이펙트를 생성

            var main            = bulletEffectInstance.main;                                    // 메인 모듈을 가져옴
            main.playOnAwake    = false;                                                        // 배치 시 자동 재생 방지
        }

        ApplyTypeDefaults(towerType);
        ApplyTowerStats(upgradeLevel);                                                          // 타워 종류에 따라 기본값 설정 및 업그레이드 단계 적용
    }

    private void Update()
    {
        RotateTowardsTarget();

        if (isSilenced && Time.time >= silencedUntil) isSilenced = false;                       // 침묵 상태가 끝났는지 확인

        if (isSilenced)
        {
            particleSwitch = false;
            // 파티클/DoT 안전 정리
            if (bulletEffectInstance && bulletEffectInstance.isPlaying) { bulletEffectInstance.Stop(); bulletEffectInstance.Clear(true); }
            if (DotDamageRoutine != null) { StopCoroutine(DotDamageRoutine); DotDamageRoutine = null; }
            return;
        }

        TryAutoBurstFire();
        // -------- 파티클 이펙트 업데이트 --------
        bool isDotType = (towerType == TowerType.Flame || towerType == TowerType.Laser);
        if (isDotType && bulletSpawnEffect == null) return;                             // 지속 데미지 타입이 아니면 종료
        if (bulletSpawnEffect == null) return;                                          // 파티클 이펙트가 없으면 종료

        if(isDotType) UpdateDotParticleDirection();                                     // 현재 바라보는 적이 없거나 네트워크로 받은 타겟이 없으면 파티클 이펙트 방향 업데이트

        if (isDotType && currentEnemy == null && (netAimTarget == null || Time.time >= netAimExpireTime))       // 현재 바라보는 적이 없고 네트워크로 받은 타겟도 없으면
            particleSwitch = false;                                                                             // 파티클 이펙트 끄기

        if (isDotType && particleSwitch)
        {
            if (!bulletEffectInstance.isPlaying)
                bulletEffectInstance.Play(true);                                                                // 파티클 이펙트가 꺼져 있다면 실행

            if (Owner != null && Owner.IsLocalPlayer && DotDamageRoutine == null)
                DotDamageRoutine = StartCoroutine(DotDamageLoop());                                             // 지속 데미지 루틴이 없으면 시작

        }
        else
        {
            if (bulletEffectInstance.isPlaying)
            {
                bulletEffectInstance.Stop();                                                                    // 파티클 이펙트가 켜져 있다면 중지
                bulletEffectInstance.Clear(true);                                                               // 파티클 이펙트 초기화
            }
            if (DotDamageRoutine != null)
            {
                StopCoroutine(DotDamageRoutine);                                                                // 지속 데미지 루틴이 실행 중이면 중지
                DotDamageRoutine = null;                                                                        // 지속 데미지 루틴 초기화
            }
        }
    }

    private void ApplyTypeDefaults(TowerType type)                                              // 타워 종류에 따라 이름 공격속도 기본값을 설정하는 메소드
    {
        switch (type)
        {
            case TowerType.Flame:
                towerName = "Flame";
                towerDisplayName = "화염방사 타워";
                attackDelay = 0.25f;
                break;
            case TowerType.Laser:
                towerName = "Laser";
                towerDisplayName = "레이저 타워";
                attackDelay = 0.25f;
                break;
            case TowerType.Machine:
                towerName = "Machine";
                towerDisplayName = "머신건 타워";
                attackDelay = 3f;
                break;
            case TowerType.Rocket:
                towerName = "Rocket";
                towerDisplayName = "로켓 타워";
                attackDelay = 8f;
                break;
            case TowerType.Multiple:
                towerName = "Multiple";
                towerDisplayName = "다연장 로켓 타워";
                attackDelay = 3f;
                break;
            default:
                towerDisplayName = "알수없는 타워";
                break;
        }
    }

    // 타워 발사 패킷을 전송하는 메소드
    private void SendFirePacket(TowerType towerType, Vector3 pos, float dmg, bool isFirst)      // 타워 발사 패킷을 전송하는 메소드
    {
        int enemyId = -1, spawnerId = -1;                                                       // 적 ID와 스포너 ID 초기화
        var enemyComp = currentEnemy ? currentEnemy.GetComponent<Enemy>() : null;               // 현재 바라보고 있는 적의 Enemy 컴포넌트를 가져옴
        if (enemyComp != null)                                                                  // 적 컴포넌트가 존재한다면
        {
            enemyId = enemyComp.EnemyId;     // Enemy.InitializeEnemy에서 세팅됨
            spawnerId = enemyComp.spawnerId; // 동일
        }

        TowerFireMessage packet = new TowerFireMessage                                          // 타워 발사 메시지 구조체 생성
        {
            towerId         = this.TowerId,
            towerType       = towerType,
            position        = pos,
            damage          = dmg,
            isFirstShot     = isFirst,
            targetEnemyId   = enemyId,
            targetSpawnerId = spawnerId
        };
        UDPClient.Instance?.SendUDP(JsonUtility.ToJson(packet), "TOWER_FIRE");                  // 타워 발사 메시지를 UDP로 전송
    }


    public void HandleEnemyInRange(Collider other)                                              // 적이 범위 내에 들어왔을 때 호출되는 메소드
    {
        // 침묵 상태면 아무것도 하지 않음
        if (isSilenced) return;

        // 이미 타겟이 있으면 유지
        if (currentEnemy == null)
        {
            var enemy = other.GetComponentInParent<Enemy>();
            if (enemy != null) currentEnemy = enemy;
        }
        if (currentEnemy == null) return;

        bool isOwner = (Owner != null && Owner.IsLocalPlayer);                                  // 로컬 플레이어 여부 확인

        switch (towerType)
        {
            case TowerType.Flame:
            case TowerType.Laser:
                // 1) 파티클이 꺼져 있다면 실행
                particleSwitch = true;
                break;

            case TowerType.Machine:
            case TowerType.Multiple:
                break;

            case TowerType.Rocket:
                // 타워 소유자가 아닌 경우 발사하지 않음
                if (!isOwner) return;
                // 로켓 발사: 8초마다 1발
                if (Time.time >= lastAttackTime + attackDelay)
                {
                    // 1) 로컬 발사체 패킷 전송
                    SendFirePacket(this.towerType, bulletSpawnPoint.position, damage, true);

                    // 2) 로컬 발사체 (데미지 적용) - replicated=false 기본값
                    var nuclearRocketGo = ObjectPoolManager.instance.NuclearRocketPool.Get();
                    nuclearRocketGo.transform.position = bulletSpawnPoint.position;
                    nuclearRocketGo.GetComponent<NuclearRocket>().SetTarget(currentEnemy.transform, damage, bulletSpawnEffect);
                    SoundManager.Instance.PlaySFX(SoundKey.Fire_Rocket);
                    lastAttackTime = Time.time;
                }
                break;
        }
    }

    public void HandleEnemyExit(Collider other)
    {
        var enemy = other.GetComponentInParent<Enemy>();
        if (currentEnemy != null && enemy == currentEnemy)
        {
            // 범위 안에 남아있는 다른 적으로 즉시 재타겟
            Enemy next = (rangeDetect != null) ? rangeDetect.GetNextTarget(transform) : null;

            if (next != null)
            {
                currentEnemy = next;

                // Flame/Laser는 DoT/파티클을 유지(혹시 꺼져 있었으면 재가동)
                if (towerType == TowerType.Flame || towerType == TowerType.Laser)
                {
                    particleSwitch = true;
                    if (Owner != null && Owner.IsLocalPlayer && DotDamageRoutine == null)
                        DotDamageRoutine = StartCoroutine(DotDamageLoop());
                }

                // 네트워크 최근 타겟도 지우지 않음 (연출 보정)
                return;
            }

            // 남은 적이 없을 때만 멈춤(현행 로직)
            currentEnemy = null;
            netAimTarget = null;

            particleSwitch = false;
            if (DotDamageRoutine != null)
            {
                StopCoroutine(DotDamageRoutine);
                DotDamageRoutine = null;
            }
        }
    }

    public void ReplicateFire(TowerFireMessage data, Transform targetOverride = null)           // 네트워크에서 받은 타워 발사 데이터를 재현하는 메소드
    {
        // 네트워크로 받은 타겟을 우선 적용
        if (targetOverride != null)
        {
            netAimTarget = targetOverride;
            netAimExpireTime = Time.time + 0.5f; // 0.3~0.7s 취향대로
        }

        // 1) 타겟 결정: 수신 시점에 넘겨준 targetOverride 우선, 없으면 현재 타워가 조준 중인 적
        Transform target = targetOverride ?? currentEnemy?.transform;
        if (target == null)
        {
            // 타겟이 완전히 없으면 안전하게 리턴(또는 근처 적 탐색 로직으로 대체 가능)
            return;
        }

        // 2) 원격 재현은 "연출만": replicated=true로 실제 데미지 처리 스킵 (Bullet/Rocket에 해당 플래그 처리 필요)
        bool replicated = true;

        switch (data.towerType)
        {
            case TowerType.Machine:
                {
                    var go = ObjectPoolManager.instance.BulletPool.Get();
                    go.transform.position = data.position;
                    float dmg = data.isFirstShot ? data.damage : data.damage * 0.5f;
                    go.GetComponent<Bullet>().SetTarget(target, dmg, replicated);                                       // replicated=true로 실제 데미지 처리 스킵
                    break;
                }

            case TowerType.Multiple:
                {
                    var go = ObjectPoolManager.instance.RocketPool.Get();
                    go.transform.position = data.position;
                    float dmg = data.isFirstShot ? data.damage : data.damage * 0.5f;                                    // 첫 발은 100% 데미지, 이후 50% 데미지
                    go.GetComponent<Rocket>().SetTarget(target, dmg, bulletSpawnEffect, replicated);                    // replicated=true로 실제 데미지 처리 스킵
                    break;
                }

            case TowerType.Rocket:
                {
                    var go = ObjectPoolManager.instance.NuclearRocketPool.Get();
                    go.transform.position = data.position;
                    go.GetComponent<NuclearRocket>().SetTarget(target, data.damage, bulletSpawnEffect, replicated);     // replicated=true로 실제 데미지 처리 스킵
                    break;
                }

            case TowerType.Flame:
            case TowerType.Laser:
                {
                    // 파티클 이펙트가 꺼져 있다면 실행
                    if (!bulletEffectInstance.isPlaying)
                        bulletEffectInstance.Play(true);
                    break;
                }
        }
    }

    // 연사관련
    private void TryAutoBurstFire()
    {
        if (isSilenced) return;                                                         // 침묵 상태면 무시
        if (towerType != TowerType.Machine && towerType != TowerType.Multiple) return;  // 연사 공격이 아닌 경우 무시
        if (Owner == null || !Owner.IsLocalPlayer) return;                              // 로컬 소유자만 실제 발사
        if (burstRoutine != null) return;                                               // 이미 진행 중인 경우 무시
        if (isBurstDelay) return;                                                       // 3초 딜레이 중인 경우 뮤사
        if (currentEnemy == null) return;                                               // 현재 적이 없는 경우 무시
        if (rangeDetect != null && !rangeDetect.Contains(currentEnemy)) return;         // 범위 밖인 경우 무시

        burstRoutine = StartCoroutine(BurstRoutine());
    }

    private IEnumerator BurstRoutine()
    {
        burstCount = 0;
        float interval = 0.1f;

        while (burstCount < 10)
        {
            // 타겟 유효성 보장
            if (currentEnemy == null || (rangeDetect != null && !rangeDetect.Contains(currentEnemy)))
            {
                currentEnemy = (rangeDetect != null) ? rangeDetect.GetNextTarget(transform) : null;
                if (currentEnemy == null) break; // 더 쏠 대상 없음 → 조기 종료
            }

            bool isFirst = (burstCount == 0);
            // 패킷(연출 재현)을 매발 보낼지 정책에 맞춰 유지/조정
            SendFirePacket(this.towerType, bulletSpawnPoint.position, damage, isFirst);

            if (Owner != null && Owner.IsLocalPlayer)
            {
                float appliedDamage = isFirst ? damage : damage * 0.5f;
                currentEnemy.TakeDamage(appliedDamage, false);
            }

            if (towerType == TowerType.Machine)
            {
                var bulletGo = ObjectPoolManager.instance.BulletPool.Get();
                bulletGo.transform.position = bulletSpawnPoint.position;
                float appliedDamage = isFirst ? damage : damage * 0.5f;
                bulletGo.GetComponent<Bullet>().SetTarget(currentEnemy.transform, appliedDamage, true); // replicated=true로 실제 데미지 처리 스킵
                SoundManager.Instance.PlaySFX(SoundKey.Fire_Bullet);
            }
            else // Multiple
            {
                var rocketGo = ObjectPoolManager.instance.RocketPool.Get();
                rocketGo.transform.position = bulletSpawnPoint.position;
                float appliedDamage = isFirst ? damage : damage * 0.5f;
                rocketGo.GetComponent<Rocket>().SetTarget(currentEnemy.transform, appliedDamage, bulletSpawnEffect, true);
                SoundManager.Instance.PlaySFX(SoundKey.Fire_Rocket);
            }

            lastAttackTime = Time.time;
            burstCount++;
            yield return new WaitForSeconds(interval);
        }

        // 버스트 종료 → 3초 딜레이
        isBurstDelay = true;
        yield return new WaitForSeconds(3f);
        isBurstDelay = false;
        burstRoutine = null;
        burstCount = 0;
    }

    private IEnumerator DotDamageLoop()                                                                 // 지속 데미지를 주는 코루틴
    {
        var delay = new WaitForSeconds(0.25f);                                                          // 0.25초 딜레이
        while (true)
        {
            if (isSilenced) { particleSwitch = false; break; }                                          // 침묵 상태면 종료
            if (currentEnemy == null || (rangeDetect != null && !rangeDetect.Contains(currentEnemy)))   // 타겟이 없거나 범위 밖(Exit 못 받은 케이스 포함) → 재타겟 or 종료
            {
                currentEnemy = (rangeDetect != null) ? rangeDetect.GetNextTarget(transform) : null;     // 범위 내 다른 적을 찾음
                if (currentEnemy == null) { particleSwitch = false; break; }                            // 이펙트 종료 신호
            }

            if (Owner != null && Owner.IsLocalPlayer)                                                   // 로컬 플레이어인 경우에만 지속 데미지 적용
            {
                bool asTrueDamage = UnityEngine.Random.value < trueDamageChance;
                currentEnemy.TakeDamage(damage * 0.5f, asTrueDamage);                                   // 지속 데미지 적용 (0.25초마다 damage의 절반 10% 확률로 방어 무시)
                if(towerType == TowerType.Flame)
                    SoundManager.Instance.PlaySFX(SoundKey.Fire_Flame);                                 // 화염방사 사운드 재생
                else if(towerType == TowerType.Laser)
                    SoundManager.Instance.PlaySFX(SoundKey.Fire_Laser);                                 // 레이저 사운드 재생
            }
            yield return delay;
        }

        if (bulletEffectInstance && bulletEffectInstance.isPlaying)                                     // 코루틴 정리
        {
            bulletEffectInstance.Stop();
            bulletEffectInstance.Clear(true);
        }
        DotDamageRoutine = null;                                                                        // 지속 데미지 루틴 초기화
    }

    private void UpdateDotParticleDirection()
    {
        // 현재 바라보는 적이 없거나 네트워크로 받은 타겟이 없으면 파티클 이펙트 중지
        bool hasAim = (currentEnemy != null) || (netAimTarget != null && Time.time < netAimExpireTime);
        if (!hasAim)
        {
            if (particleSwitch) particleSwitch = false; // Update()의 else 블록에서 Stop/Clear 실행
            return;
        }

        Transform aim = null;

        // 1) 로컬 트리거로 잡힌 타겟 우선
        if (currentEnemy != null) aim = currentEnemy.transform;

        // 2) 없다면 네트워크로 받은 최근 타겟 사용(짧은 시간)
        else if (netAimTarget != null && Time.time < netAimExpireTime) aim = netAimTarget;

        // 3) 둘 다 없다면 타워의 부모 오브젝트를 바라봄 (대기 상태)
        if (aim != null && bulletEffectInstance != null && bulletEffectInstance.isPlaying)
        {
            Vector3 dir = (aim.position - bulletSpawnPoint.position).normalized;                // 타겟 방향 계산
            var rot = Quaternion.LookRotation(dir, Vector3.up);                                 // 타겟 방향으로 회전
            bulletEffectInstance.transform.rotation = Quaternion.Slerp(
                bulletEffectInstance.transform.rotation, rot, Time.deltaTime * 20f);            // 부드럽게 회전
        }
    }

    // 타워가 바라보는 적을 부드럽게 회전시키는 메소드
    private void RotateTowardsTarget()
    {
        Transform aim = null;

        // 1) 로컬 트리거로 잡힌 타겟 우선
        if (currentEnemy != null) aim = currentEnemy.transform;
        // 2) 없다면 네트워크로 받은 최근 타겟 사용(짧은 시간)
        else if (netAimTarget != null && Time.time < netAimExpireTime) aim = netAimTarget;

        // 3) 둘 다 없다면 타워의 부모 오브젝트를 바라봄 (대기 상태)
        if (aim != null)
        {
            Vector3 dir = (aim.position - transform.position).normalized;
            var targetRot = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z));
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }
        // 4) 타겟이 없으면 idle 회전
        else
        {
            transform.Rotate(Vector3.up * idleRotationSpeed * Time.deltaTime);
        }
    }

    // 타워를 즉시 멈추고 duration 동안 공격 금지
    public void ForceStopAttackAndSilence(float duration)
    {
        isSilenced = true;
        silencedUntil = Time.time + duration;

        // 진행 중 공격 강제 종료
        particleSwitch = false;
        if (DotDamageRoutine != null) { StopCoroutine(DotDamageRoutine); DotDamageRoutine = null; }
        if (bulletEffectInstance && bulletEffectInstance.isPlaying) { bulletEffectInstance.Stop(); bulletEffectInstance.Clear(true); }

        if (burstRoutine != null) { StopCoroutine(burstRoutine); burstRoutine = null; burstCount = 0; isBurstDelay = false; }
    }

    // ----------------------------- 업그레이드 데이터 로드 -----------------------------

    // 시작 시 한 번만 로드되도록
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureLoaded() => LoadUpgradeData();                        // 게임 시작 시 업그레이드 데이터 로드

    public static void LoadUpgradeData()                                            // 업그레이드 데이터를 로드하는 메소드
    {
        if (isDataLoaded && upgradeData != null) return;

        // Resources/Data/tower_upgrade_damages_costs.json 로 넣어두기 (TextAsset)
        var ta = Resources.Load<TextAsset>("Data/tower_upgrade_damages_costs");
        if (ta == null) { Debug.LogError("업그레이드 JSON을 Resources에 넣으세요: Resources/Data/tower_upgrade_damages_costs.json"); return; }

        upgradeData = JsonUtility.FromJson<UpgradeData>(ta.text);
        if (upgradeData?.TowerUpgrade != null)
        {
            isDataLoaded = true;
            Debug.Log($"[TowerController] 업그레이드 데이터 로드 완료: {upgradeData.TowerUpgrade.Length} 단계");
        }
        else
        {
            Debug.LogError("[TowerController] 업그레이드 JSON 파싱 실패");
        }
    }

    // 업그레이드 데이터를 가져오는 메소드
    public static UpgradeLevel StaticGetUpgradeData(int level)
    {
        foreach (var data in upgradeData.TowerUpgrade)
        {
            if (data.Level == level)
                return data;
        }
        return null;
    }

    // 타워 비용을 가져오는 메소드
    public static float StaticGetTowerCost(UpgradeLevel data, TowerType towerType)
    {
        switch (towerType)
        {
            case TowerType.Rocket:      return data.RocketCost;
            case TowerType.Multiple:    return data.MultipleCost;
            case TowerType.Machine:     return data.MachineCost;
            case TowerType.Laser:       return data.LaserCost;
            case TowerType.Flame:       return data.FlameCost;
            default:                    return 0;
        }
    }

    // 타워의 공격력과 비용을 가져오는 메소드
    public static TowerStats GetTowerStats(UpgradeLevel data, TowerType towerType)
    {
        TowerStats stats = new TowerStats();

        switch (towerType)
        {
            case TowerType.Rocket:
                stats.Damage    = data.Rocket;
                stats.Cost      = data.RocketCost;
                break;
            case TowerType.Multiple:
                stats.Damage    = data.Multiple;
                stats.Cost      = data.MultipleCost;
                break;
            case TowerType.Machine:
                stats.Damage    = data.Machine;
                stats.Cost      = data.MachineCost;
                break;
            case TowerType.Laser:
                stats.Damage    = data.Laser;
                stats.Cost      = data.LaserCost;
                break;
            case TowerType.Flame:
                stats.Damage    = data.Flame;
                stats.Cost      = data.FlameCost;
                break;
            default:
                stats.Damage    = 0;
                stats.Cost      = 0;
                break;
        }
        return stats;
    }

    // 업그레이드 단계에 따라 타워의 공격력과 비용을 적용하는 메소드
    public void ApplyTowerStats(int level)
    {
        LoadUpgradeData();                                                              // 혹시나 업그레이드 데이터가 로드되지 않았다면 로드
        var data = StaticGetUpgradeData(level);
        if (data == null) { Debug.LogWarning($"레벨 {level} 데이터 없음"); return; }
        var stats = GetTowerStats(data, towerType);
        damage = stats.Damage;
        Debug.Log($"[타워 데미지 갱신] {towerType} → Lv.{level}, Damage: {damage}");
    }

    // ITowerSelectable 인터페이스 구현
    public void OnSelected()
    {
        UIManager.Instance.UpdateBuildingStatus(this, GetComponentInParent<TowerBase>());
        if (Owner != null)
        {
            UIManager.Instance.SetUpgradeUI(Owner.PlayerIndex, false);
            UIManager.Instance.ToggleSelectTowerPanel(true);
        }
    }

    public void OnDeSelected()
    {
        if (UIManager.Instance != null)
        {
            UIManager.Instance.CloseBuildingStatus();
            UIManager.Instance.ToggleSelectTowerPanel(false);
        }
    }
}
