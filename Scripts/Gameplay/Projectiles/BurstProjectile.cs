using UnityEngine;
using UnityEngine.Pool;

/// <summary>고정 표적 추적, 단일 명중, 사망 시 풀 반환을 연사 탄환끼리 공유합니다.</summary>
public abstract class BurstProjectile : MonoBehaviour
{
    public IObjectPool<GameObject> Pool { get; set; }

    [SerializeField]
    private float speed = 100f;
    private Enemy target;
    private float damage;
    private float presentationDuration;
    private bool visualOnly;
    private int attackerIndex;
    private bool released;
    private float expiresAt;
    protected virtual Quaternion ModelRotation => Quaternion.identity;
    protected abstract float StunDuration { get; }
    // 발마다 15%였던 판정을 한 번으로 모으되, 10발 중 한 번 이상 성공할 확률을 유지합니다.
    public static float BurstStunChance => 1f - Mathf.Pow(0.85f, TowerController.BurstShotCount);

    protected virtual void OnEnable()
    {
        released = false;
        expiresAt = Time.time + 5f;
    }

    protected void Configure(Transform enemyTarget, float totalDamage, bool replicated, int ownerIndex, float displayDuration)
    {
        DetachTarget();
        target = enemyTarget != null ? enemyTarget.GetComponentInParent<Enemy>() : null;
        damage = totalDamage;
        visualOnly = replicated;
        attackerIndex = ownerIndex;
        presentationDuration = displayDuration;
        if (target == null || !target.IsAlive)
        {
            ReturnToPool();
            return;
        }

        target.TargetUnavailable += ReturnToPool;
    }

    protected virtual void Update()
    {
        if (released)
            return;
        if (target == null || !target.IsAlive || Time.time >= expiresAt)
        {
            ReturnToPool();
            return;
        }

        Vector3 destination = target.transform.position;
        Vector3 direction = destination - transform.position;
        float step = speed * Time.deltaTime;
        if (direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(direction) * ModelRotation;
        transform.position = Vector3.MoveTowards(transform.position, destination, step);
        // 물리 프레임 사이에 콜라이더를 관통해도 지정 표적에 한 번 명중합니다.
        if (direction.sqrMagnitude <= step * step)
            HitTarget();
    }

    protected virtual void OnTriggerEnter(Collider other)
    {
        if (!released && target != null && other.GetComponentInParent<Enemy>() == target)
            HitTarget();
    }

    private void HitTarget()
    {
        if (released || target == null || !target.IsAlive)
        {
            ReturnToPool();
            return;
        }

        Enemy hit = target;
        // 피해 콜백에서 사망 이벤트가 발생해도 이 탄환은 두 번 반환하지 않습니다.
        released = true;
        DetachTarget();
        PlayImpact();
        if (!visualOnly && damage > 0f)
            hit.TakeDamage(damage, false, true, attackerIndex, BurstStunChance, StunDuration, presentationDuration);
        ReleaseObject();
    }

    protected virtual void PlayImpact()
    {
    }

    private void ReturnToPool()
    {
        if (released)
            return;
        released = true;
        DetachTarget();
        ReleaseObject();
    }

    private void ReleaseObject()
    {
        if (Pool != null)
            Pool.Release(gameObject);
        else
            Destroy(gameObject);
    }

    private void DetachTarget()
    {
        if (target != null)
            target.TargetUnavailable -= ReturnToPool;
        target = null;
    }

    protected virtual void OnDisable()
    {
        DetachTarget();
        released = true;
        damage = 0f;
        presentationDuration = 0f;
    }
}
