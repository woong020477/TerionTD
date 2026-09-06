using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>타워의 표적 선택·공격 주기·원격 발사 연출을 조정합니다. 업그레이드 조회는 데이터 계층에 위임합니다.</summary>
public class TowerController : MonoBehaviour, ITowerSelectable
{
    public int TowerId { get; private set; }
    public int OwnerIndex { get; private set; } = -1;
    public NetworkEntityKey NetworkKey => new NetworkEntityKey(OwnerIndex, TowerId);
    public PlayerController Owner { get; private set; }
    public TowerType towerType { get; set; }

    public static readonly Dictionary<NetworkEntityKey, TowerController> ById = new();
    [Header("총알 생성 지점과 이팩트 관련")]
    public Transform bulletSpawnPoint;
    [SerializeField]
    private ParticleSystem bulletSpawnEffect; // 총알 발사 시 이펙트
    private ParticleSystem bulletEffectInstance; // 발사 이펙트 인스턴스
    private bool particleSwitch = false; // 파티클 이펙트 토글 true면 켜짐, false면 꺼짐
    [Header("타워 이름")]
    public string towerName; // 타워 이름 (예: "Frame", "Laser", "Machine", "Rocket", "Multiple" 등)
    public string towerDisplayName; // UI에 표시될 타워 이름
    [Header("업그레이드 단계 및 능력치")]
    public int upgradeLevel = 1; // 현재 업그레이드 단계
    public float damage = 10; // 공격력
    public float attackDelay = 1.0f; // 공격 간격(초 단위)
    // 연사 후 딜레이
    public const int BurstShotCount = 10;
    public const float BurstShotInterval = 0.1f;
    public static float GetBurstDamage(float shotDamage) => shotDamage * (1f + (BurstShotCount - 1) * 0.5f);
    private bool isBurstDelay = false; // 3초 딜레이 상태인지 여부
    private int nextBurstSequence;
    private int lastRemoteBurstSequence;
    private Coroutine remoteBurstRoutine;
    private Enemy burstTarget;
    private Enemy currentEnemy; // 현재 바라보고 있는 적 Enemy 컴포넌트
    private TowerRangeDetect rangeDetect; // 범위 감지 컴포넌트
    private TowerRangeIndicator rangeIndicator;
    private float lastAttackTime = 0f; // 마지막 공격 시간 기록
    private float rotationSpeed = 90f; // 회전 속도
    private float idleRotationSpeed = 30f; // 적이 없을 때 대기 회전 속도(도/초)
    private bool isSilenced; // 침묵 상태인지 여부
    private float silencedUntil; // 침묵 상태가 끝나는 시간
    // 확률
    [Range(0f, 1f)]
    private float trueDamageChance = 0.10f;
    // 정적 데이터
    // 코루틴
    private Coroutine DotDamageRoutine; // 파티클 데미지 루틴
    private Coroutine burstRoutine; // 연사 공격 루틴
    private Transform netAimTarget; // 네트워크로 받은 최근 타겟
    private float netAimExpireTime = 0; // 타겟 유지 만료 시각 (Time.time)
    public static List<TowerController> Towers = new List<TowerController>(); // 모든 타워를 저장하는 리스트
    public void InitializeNetwork(int ownerIndex, int id, PlayerController owner)
    {
        var previousKey = NetworkKey;
        if (previousKey.IsValid && ById.TryGetValue(previousKey, out var previous) && previous == this)
            ById.Remove(previousKey);
        OwnerIndex = ownerIndex;
        TowerId = id;
        Owner = owner;
        var key = NetworkKey;
        if (ById.TryGetValue(key, out var existing) && existing != this)
            Debug.LogWarning($"[TowerController] 중복 네트워크 키 {key}를 새 타워로 교체합니다.");
        ById[key] = this;
    }

    public static bool TryGetNetworkTower(int ownerIndex, int towerId, out TowerController tower)
    {
        return ById.TryGetValue(new NetworkEntityKey(ownerIndex, towerId), out tower);
    }

    // 타워가 활성화될 때 호출되는 메소드
    private void OnEnable()
    {
        if (!Towers.Contains(this))
            Towers.Add(this);
    }

    /// <summary>재사용·씬 종료 뒤에 공격 루틴과 조준 참조가 남지 않도록 정리합니다.</summary>
    private void OnDisable()
    {
        StopAllCoroutines();
        burstRoutine = null;
        remoteBurstRoutine = null;
        DotDamageRoutine = null;
        burstTarget = null;
        currentEnemy = null;
        netAimTarget = null;
        particleSwitch = false;
        isBurstDelay = false;
        if (bulletEffectInstance != null)
            bulletEffectInstance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        Towers.Remove(this);
        var key = NetworkKey;
        if (key.IsValid && ById.TryGetValue(key, out var registered) && registered == this)
            ById.Remove(key);
    }

    public void SetOwner(PlayerController player) => Owner = player;
    private void Awake()
    {
        LoadUpgradeData();
        EnsureRangeIndicator();
    }

    private void EnsureRangeIndicator()
    {
        if (!rangeDetect)
            rangeDetect = GetComponentInChildren<TowerRangeDetect>(true);
        if (rangeDetect != null && rangeDetect.TryGetComponent(out SphereCollider rangeCollider))
        {
            rangeIndicator = GetComponent<TowerRangeIndicator>();
            if (rangeIndicator == null)
                rangeIndicator = gameObject.AddComponent<TowerRangeIndicator>();
            rangeIndicator.Initialize(rangeCollider);
        }
    }

    private void Start()
    {
        // 화염/레이저의 지속 이펙트만 타워에 붙입니다.
        // 로켓 폭발은 명중 위치에서 생성해야 부모 배율로 커진 이펙트 콜라이더가 타워를 둘러싸지 않습니다.
        bool isDotType = towerType == TowerType.Flame || towerType == TowerType.Laser;
        if (isDotType && bulletSpawnPoint != null && bulletSpawnEffect != null)
        {
            bulletEffectInstance = Instantiate(bulletSpawnEffect, bulletSpawnPoint.position, Quaternion.identity, transform);
            var main = bulletEffectInstance.main;
            main.playOnAwake = false;
        }

        ApplyTypeDefaults(towerType);
        ApplyTowerStats(upgradeLevel);
    }

    private void Update()
    {
        RotateTowardsTarget();
        if (isSilenced && Time.time >= silencedUntil)
            isSilenced = false;
        if (isSilenced)
        {
            particleSwitch = false;
            // 파티클/DoT 안전 정리
            if (bulletEffectInstance && bulletEffectInstance.isPlaying)
            {
                bulletEffectInstance.Stop();
                bulletEffectInstance.Clear(true);
            }

            if (DotDamageRoutine != null)
            {
                StopCoroutine(DotDamageRoutine);
                DotDamageRoutine = null;
            }

            return;
        }

        TryAutoBurstFire();
        bool isDotType = (towerType == TowerType.Flame || towerType == TowerType.Laser);
        if (!isDotType || bulletEffectInstance == null)
            return;
        if (isDotType)
            UpdateDotParticleDirection();
        if (isDotType && currentEnemy == null && (netAimTarget == null || Time.time >= netAimExpireTime))
            particleSwitch = false;
        if (isDotType && particleSwitch)
        {
            if (!bulletEffectInstance.isPlaying)
                bulletEffectInstance.Play(true);
            if (Owner != null && Owner.IsLocalPlayer && DotDamageRoutine == null)
                DotDamageRoutine = StartCoroutine(DotDamageLoop());
        }
        else
        {
            if (bulletEffectInstance.isPlaying)
            {
                bulletEffectInstance.Stop();
                bulletEffectInstance.Clear(true);
            }

            if (DotDamageRoutine != null)
            {
                StopCoroutine(DotDamageRoutine);
                DotDamageRoutine = null;
            }
        }
    }

    private void ApplyTypeDefaults(TowerType type)
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
    private void SendFirePacket(TowerType towerType, Vector3 pos, float dmg, bool isFirst, Enemy shotTarget = null, int shotCount = 1, int burstSequence = 0)
    {
        int enemyId = -1, spawnerId = -1;
        var enemyComp = shotTarget != null ? shotTarget : currentEnemy;
        if (enemyComp != null)
        {
            enemyId = enemyComp.EnemyId;
            spawnerId = enemyComp.spawnerId;
        }

        TowerFireMessage packet = new TowerFireMessage
        {
            ownerIndex = OwnerIndex,
            towerId = this.TowerId,
            towerType = towerType,
            position = pos,
            damage = dmg,
            isFirstShot = isFirst,
            targetEnemyId = enemyId,
            targetSpawnerId = spawnerId,
            burstShotCount = shotCount,
            burstSequence = burstSequence
        };
        UDPClient.Instance?.SendUDP(JsonUtility.ToJson(packet), "TOWER_FIRE");
    }

    public void HandleEnemyInRange(Collider other)
    {
        // 침묵 상태면 아무것도 하지 않음
        if (isSilenced)
            return;
        // 이미 타겟이 있으면 유지
        if (currentEnemy == null || !currentEnemy.IsAlive)
        {
            var enemy = other.GetComponentInParent<Enemy>();
            if (enemy != null && enemy.IsAlive)
                currentEnemy = enemy;
        }

        if (currentEnemy == null || !currentEnemy.IsAlive)
            return;
        bool isOwner = (Owner != null && Owner.IsLocalPlayer);
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
                if (!isOwner)
                    return;
                // 로켓 발사: 8초마다 1발
                if (Time.time >= lastAttackTime + attackDelay)
                {
                    // 1) 로컬 발사체 패킷 전송
                    SendFirePacket(this.towerType, bulletSpawnPoint.position, damage, true);
                    // 2) 로컬 발사체 (데미지 적용) - replicated=false 기본값
                    var nuclearRocketGo = ObjectPoolManager.instance.NuclearRocketPool.Get();
                    nuclearRocketGo.transform.position = bulletSpawnPoint.position;
                    nuclearRocketGo.GetComponent<NuclearRocket>().SetTarget(currentEnemy.transform, damage, bulletSpawnEffect, false, OwnerIndex);
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

    /// <summary>원격 발사는 패킷에 지정된 살아 있는 적에게만 재현하며 다른 적으로 대체하지 않습니다.</summary>
    public void ReplicateFire(TowerFireMessage data, Transform targetOverride = null)
    {
        if (!isActiveAndEnabled || isSilenced || (Owner != null && Owner.IsLocalPlayer))
            return;
        Enemy target = targetOverride != null ? targetOverride.GetComponentInParent<Enemy>() : null;
        if (target == null || !target.IsAlive || target.EnemyId != data.targetEnemyId || target.spawnerId != data.targetSpawnerId)
            return;
        bool isBurst = data.towerType == TowerType.Machine || data.towerType == TowerType.Multiple;
        if (isBurst && data.burstSequence > 0 && data.burstSequence <= lastRemoteBurstSequence)
            return;
        netAimTarget = target.transform;
        netAimExpireTime = Time.time + 1.5f;
        switch (data.towerType)
        {
            case TowerType.Machine:
            case TowerType.Multiple:
                if (data.burstSequence > 0)
                    lastRemoteBurstSequence = data.burstSequence;
                if (remoteBurstRoutine != null)
                    StopCoroutine(remoteBurstRoutine);
                // 구 버전의 단발 패킷은 한 발로만 재현합니다. 모든 테스트 클라이언트는 함께 갱신해야 합니다.
                int shots = data.burstShotCount > 0 ? Mathf.Clamp(data.burstShotCount, 1, BurstShotCount) : 1;
                remoteBurstRoutine = StartCoroutine(PlayRemoteBurst(target, data, shots));
                break;
            case TowerType.Rocket:
                var go = ObjectPoolManager.instance.NuclearRocketPool.Get();
                go.transform.position = data.position;
                go.GetComponent<NuclearRocket>().SetTarget(target.transform, data.damage, bulletSpawnEffect, true);
                break;
            case TowerType.Flame:
            case TowerType.Laser:
                particleSwitch = true;
                if (bulletEffectInstance != null && !bulletEffectInstance.isPlaying)
                    bulletEffectInstance.Play(true);
                break;
        }
    }

    private void TryAutoBurstFire()
    {
        if (isSilenced || (towerType != TowerType.Machine && towerType != TowerType.Multiple))
            return;
        if (Owner == null || !Owner.IsLocalPlayer || burstRoutine != null || isBurstDelay)
            return;
        // 다음 버스트를 시작할 때만 새 표적을 선정합니다.
        if (currentEnemy == null || !currentEnemy.IsAlive || (rangeDetect != null && !rangeDetect.Contains(currentEnemy)))
            currentEnemy = rangeDetect != null ? rangeDetect.GetNextTarget(transform) : null;
        if (currentEnemy == null || !currentEnemy.IsAlive || bulletSpawnPoint == null)
            return;
        burstRoutine = StartCoroutine(BurstRoutine());
    }

    /// <summary>버스트 동안 표적을 고정하고 첫 명중에만 합산 피해를 부여합니다. 나머지 탄은 연출입니다.</summary>
    private IEnumerator BurstRoutine()
    {
        Enemy fixedTarget = currentEnemy;
        burstTarget = fixedTarget;
        float totalDamage = GetBurstDamage(damage);
        SendFirePacket(towerType, bulletSpawnPoint.position, totalDamage, true, fixedTarget, BurstShotCount, ++nextBurstSequence);
        var interval = new WaitForSeconds(BurstShotInterval);
        for (int shot = 0; shot < BurstShotCount; shot++)
        {
            if (isSilenced || fixedTarget == null || !fixedTarget.IsAlive)
                break;
            // 첫 탄환의 명중만 실제 피해를 발생시키고, 나머지는 네트워크 요청 없는 연출입니다.
            SpawnBurstProjectile(fixedTarget, towerType, bulletSpawnPoint.position, shot == 0 ? totalDamage : 0f, shot != 0);
            lastAttackTime = Time.time;
            yield return interval;
        }

        burstTarget = null;
        isBurstDelay = true;
        yield return new WaitForSeconds(attackDelay);
        isBurstDelay = false;
        burstRoutine = null;
    }

    /// <summary>발사 패킷 하나를 여러 탄환으로 재현하며 원격 연출에서 피해 요청은 만들지 않습니다.</summary>
    private IEnumerator PlayRemoteBurst(Enemy fixedTarget, TowerFireMessage data, int shots)
    {
        burstTarget = fixedTarget;
        var interval = new WaitForSeconds(BurstShotInterval);
        for (int shot = 0; shot < shots; shot++)
        {
            if (isSilenced || fixedTarget == null || !fixedTarget.IsAlive)
                break;
            Vector3 position = bulletSpawnPoint != null ? bulletSpawnPoint.position : data.position;
            SpawnBurstProjectile(fixedTarget, data.towerType, position, 0f, true);
            yield return interval;
        }

        burstTarget = null;
        remoteBurstRoutine = null;
    }

    private void SpawnBurstProjectile(Enemy target, TowerType type, Vector3 position, float totalDamage, bool visualOnly)
    {
        float displayDuration = (BurstShotCount - 1) * BurstShotInterval;
        if (type == TowerType.Machine)
        {
            var go = ObjectPoolManager.instance.BulletPool.Get();
            go.transform.position = position;
            go.GetComponent<Bullet>().SetTarget(target.transform, totalDamage, visualOnly, OwnerIndex, displayDuration);
        }
        else
        {
            var go = ObjectPoolManager.instance.RocketPool.Get();
            go.transform.position = position;
            go.GetComponent<Rocket>().SetTarget(target.transform, totalDamage, bulletSpawnEffect, visualOnly, OwnerIndex, displayDuration);
        }

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(type == TowerType.Machine ? SoundKey.Fire_Bullet : SoundKey.Fire_Rocket);
    }

    /// <summary>소유자 측에서만 지속 피해를 요청합니다. 적 HP의 최종 변경은 호스트가 담당합니다.</summary>
    private IEnumerator DotDamageLoop()
    {
        var delay = new WaitForSeconds(0.25f);
        while (true)
        {
            if (isSilenced)
            {
                particleSwitch = false;
                break;
            }

            if (currentEnemy == null || !currentEnemy.IsAlive || (rangeDetect != null && !rangeDetect.Contains(currentEnemy)))
            {
                currentEnemy = (rangeDetect != null) ? rangeDetect.GetNextTarget(transform) : null;
                if (currentEnemy == null)
                {
                    particleSwitch = false;
                    break;
                }
            }

            if (Owner != null && Owner.IsLocalPlayer)
            {
                bool asTrueDamage = UnityEngine.Random.value < trueDamageChance;
                currentEnemy.TakeDamage(damage * 0.5f, asTrueDamage, true, OwnerIndex);
                if (towerType == TowerType.Flame)
                    SoundManager.Instance.PlaySFX(SoundKey.Fire_Flame);
                else if (towerType == TowerType.Laser)
                    SoundManager.Instance.PlaySFX(SoundKey.Fire_Laser);
            }

            yield return delay;
        }

        if (bulletEffectInstance && bulletEffectInstance.isPlaying)
        {
            bulletEffectInstance.Stop();
            bulletEffectInstance.Clear(true);
        }

        DotDamageRoutine = null;
    }

    private void UpdateDotParticleDirection()
    {
        // 현재 바라보는 적이 없거나 네트워크로 받은 타겟이 없으면 파티클 이펙트 중지
        bool hasAim = (currentEnemy != null) || (netAimTarget != null && Time.time < netAimExpireTime);
        if (!hasAim)
        {
            if (particleSwitch)
                particleSwitch = false;
            return;
        }

        Transform aim = null;
        // 1) 로컬 트리거로 잡힌 타겟 우선
        if (currentEnemy != null)
            aim = currentEnemy.transform;
        // 2) 없다면 네트워크로 받은 최근 타겟 사용(짧은 시간)
        else if (netAimTarget != null && Time.time < netAimExpireTime)
            aim = netAimTarget;
        // 3) 둘 다 없다면 타워의 부모 오브젝트를 바라봄 (대기 상태)
        if (aim != null && bulletEffectInstance != null && bulletEffectInstance.isPlaying)
        {
            Vector3 dir = (aim.position - bulletSpawnPoint.position).normalized;
            var rot = Quaternion.LookRotation(dir, Vector3.up);
            bulletEffectInstance.transform.rotation = Quaternion.Slerp(bulletEffectInstance.transform.rotation, rot, Time.deltaTime * 20f);
        }
    }

    // 타워가 바라보는 적을 부드럽게 회전시키는 메소드
    private void RotateTowardsTarget()
    {
        Transform aim = null;
        // 1) 로컬 트리거로 잡힌 타겟 우선
        if (burstTarget != null && burstTarget.IsAlive)
            aim = burstTarget.transform;
        else if (currentEnemy != null && currentEnemy.IsAlive)
            aim = currentEnemy.transform;
        // 2) 없다면 네트워크로 받은 최근 타겟 사용(짧은 시간)
        else if (netAimTarget != null && Time.time < netAimExpireTime)
            aim = netAimTarget;
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
        if (DotDamageRoutine != null)
        {
            StopCoroutine(DotDamageRoutine);
            DotDamageRoutine = null;
        }

        if (bulletEffectInstance && bulletEffectInstance.isPlaying)
        {
            bulletEffectInstance.Stop();
            bulletEffectInstance.Clear(true);
        }

        if (burstRoutine != null)
        {
            StopCoroutine(burstRoutine);
            burstRoutine = null;
            isBurstDelay = false;
        }

        if (remoteBurstRoutine != null)
        {
            StopCoroutine(remoteBurstRoutine);
            remoteBurstRoutine = null;
        }

        burstTarget = null;
    }

    // 시작 시 한 번만 로드되도록
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureLoaded() => LoadUpgradeData();
    // 기존 호출부를 유지하는 호환 진입점이며, 데이터 조회 책임은 카탈로그에 있습니다.
    public static void LoadUpgradeData() => TowerUpgradeCatalog.Load();
    public static UpgradeLevel StaticGetUpgradeData(int level) => TowerUpgradeCatalog.GetLevel(level);
    public static float StaticGetTowerCost(UpgradeLevel data, TowerType towerType) => TowerUpgradeCatalog.GetCost(data, towerType);
    public static TowerStats GetTowerStats(UpgradeLevel data, TowerType towerType) => TowerUpgradeCatalog.GetStats(data, towerType);
    // 업그레이드 단계에 따라 타워의 공격력과 비용을 적용하는 메소드
    public void ApplyTowerStats(int level)
    {
        LoadUpgradeData();
        var data = StaticGetUpgradeData(level);
        if (data == null)
        {
            Debug.LogWarning($"레벨 {level} 데이터 없음");
            return;
        }

        var stats = GetTowerStats(data, towerType);
        damage = stats.Damage;
    }

    // ITowerSelectable 인터페이스 구현
    public void OnSelected()
    {
        EnsureRangeIndicator();
        rangeIndicator?.Show();
        UIManager.Instance.UpdateBuildingStatus(this, GetComponentInParent<TowerBase>());
        bool isOwnedByLocal = Owner != null && Owner.IsLocalPlayer;
        if (isOwnedByLocal)
            UIManager.Instance.SetUpgradeUI(Owner.PlayerIndex, false);
        UIManager.Instance.ToggleSelectTowerPanel(true);
    }

    public void OnDeSelected()
    {
        rangeIndicator?.Hide();
        if (UIManager.Instance != null)
        {
            UIManager.Instance.CloseBuildingStatus();
            UIManager.Instance.ToggleSelectTowerPanel(false);
        }
    }
}
