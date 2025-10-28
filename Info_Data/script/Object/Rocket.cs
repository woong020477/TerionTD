using System.Collections;
using UnityEngine;
using UnityEngine.Pool;

public class Rocket : MonoBehaviour
{
    public IObjectPool<GameObject> Pool { get; set; }

    [Header("총알 스피드")]
    [SerializeField] private float speed = 100f;

    [Header("총알 데미지")]
    private float damage;

    private Transform target;
    private bool isReleased = false;
    private ParticleSystem explosionEffectPrefab;

    [Range(0f, 1f)] private float StunChance = 0.15f;     // 15%
    private float StunDuration = 3f;                      // 3초

    // 총알이 복제되었는지 여부를 나타내는 플래그
    private bool isReplicated = false;

    // 레이어 마스크를 사용하여 적과 보스 레이어 설정
    private static int enemyLayer = -1;
    private static int bossLayer = -1;

    // 마지막 이동 방향 저장용 변수
    private Vector3 lastDirection;

    // 폭발 후 파괴 지연 시간
    private float destructionDelay = 1.5f;
    private Coroutine destructionCoroutine;
    private bool isDestruction;

    private void Awake()
    {
        if (enemyLayer == -1) enemyLayer = LayerMask.NameToLayer("Enemy");
        if (bossLayer == -1) bossLayer = LayerMask.NameToLayer("Boss");
    }

    void Update()
    {
        Vector3 direction;

        if (target != null)
        {
            // 목표 방향 계산
            direction = (target.position - transform.position).normalized;
            lastDirection = direction;

            CancelDestruction(); // 목표물이 있으면 파괴 코루틴 취소
        }

        else
        {
            direction = lastDirection;

            Destruction(); // 목표물이 없으면 파괴 시작
        }

        // 적을 향해 이동
        transform.position += direction * speed * Time.deltaTime;

        // 이동 방향으로 회전
        if (direction != Vector3.zero) 
        {
            Quaternion lookRotation = Quaternion.LookRotation(direction);
            transform.rotation = lookRotation;
        }
    }

    private void OnEnable()
    {
        isReleased      = false;                // 풀에서 나올 때 플래그 리셋
        isReplicated    = false;                // 풀에서 나올 때 플래그 리셋
        lastDirection   = transform.forward;    // 초기 방향 설정

        CancelDestruction();                    // 파괴 코루틴 취소
    }

    public void SetTarget(Transform enemyTarget, float rocket_Damage, ParticleSystem explosionParticle, bool replicated = false)
    {
        target                  = enemyTarget;           // 적 위치
        damage                  = rocket_Damage;         // 데미지
        explosionEffectPrefab   = explosionParticle;     // 폭발 이펙트
        isReplicated            = replicated;            // 원격 재현 여부
    }

    // 파괴 시작
    private void Destruction()
    {
        if (isDestruction) return;
        isDestruction = true;
        destructionCoroutine = StartCoroutine(DestructionCoroutine(destructionDelay));
    }

    // 파괴 코루틴 취소
    private void CancelDestruction()
    {
        if (!isDestruction) return;
        if (destructionCoroutine != null)
        {
            StopCoroutine(destructionCoroutine);
            destructionCoroutine = null;
            isDestruction = false;
        }
    }

    // 일정 시간 후에 오브젝트를 회수하거나 파괴하는 코루틴
    private IEnumerator DestructionCoroutine(float delay)
    {
        yield return new WaitForSeconds(delay);

        // 이미 다른 이유로 회수되었으면 무시
        if (isReleased) yield break;

        isReleased = true;
        if (Pool != null) Pool.Release(gameObject);
        else Destroy(gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isReleased) return;

        int otherLayer = other.gameObject.layer;
        if (otherLayer == enemyLayer || otherLayer == bossLayer)
        {
            // 폭발 이펙트 (연출)
            if (explosionEffectPrefab != null)
            {
                var ps = Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
                ps.Play();
                Destroy(ps.gameObject, ps.main.duration + ps.main.startLifetime.constantMax);

                SoundManager.Instance.PlaySFX(SoundKey.Fire_Boom);
            }
            // 데미지 적용
            if (!isReplicated)
            {
                int mask = (1 << enemyLayer) | (1 << bossLayer);
                var hits = Physics.OverlapSphere(transform.position, 2f, mask, QueryTriggerInteraction.Collide);

                foreach (var hit in hits)
                {
                    var enemy = hit.GetComponentInParent<Enemy>();
                    if (enemy == null) continue;


                    enemy.TakeDamage(damage, false);

                    // 15% 확률로 3초 스턴
                    if (UnityEngine.Random.value < StunChance)
                        enemy.Stun(StunDuration);
                }
            }
        }

        isReleased = true;
        if (Pool != null) Pool.Release(gameObject);
        else Destroy(gameObject);
    }
}
