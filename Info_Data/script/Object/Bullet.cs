using System.Collections;
using UnityEngine;
using UnityEngine.Pool;

public class Bullet : MonoBehaviour
{
    public IObjectPool<GameObject> Pool { get; set; }

    [Header("총알 스피드")]
    [SerializeField] private float speed = 100f;

    [Header("총알 데미지")]
    private float damage;

    private Transform target;

    [Range(0f, 1f)] private float StunChance = 0.15f;     // 15%
    private float StunDuration = 2f;                      // 2초

    private bool isReleased = false;

    // 총알이 복제되었는지 여부를 나타내는 플래그
    private bool isReplicated = false;

    // 레이어 마스크를 사용하여 적과 보스 레이어 설정
    private static int enemyLayer = -1;
    private static int bossLayer = -1;

    private Vector3 lastDirection;

    // 폭발 후 파괴 지연 시간
    private float destructionDelay = 1.5f;
    private Coroutine destructionCoroutine;
    private bool isDestruction;

    private void Awake()
    {
        // 최초 한 번만 레이어 캐싱
        if (enemyLayer == -1) enemyLayer = LayerMask.NameToLayer("Enemy");
        if (bossLayer == -1) bossLayer = LayerMask.NameToLayer("Boss");
    }

    void Update()
    {
        Vector3 direction;

        if (target != null)
        {
            direction = (target.position - transform.position).normalized;
            lastDirection = direction;

            CancelDestruction(); // 목표물이 있으면 파괴 코루틴 취소
        }
        else
        {
            direction = lastDirection;

            Destruction(); // 목표물이 없으면 파괴 시작
        }
        transform.position += direction * speed * Time.deltaTime;

        if (direction != Vector3.zero)
        {
            Quaternion lookRotation = Quaternion.LookRotation(direction);
            transform.rotation = lookRotation * Quaternion.Euler(90f, 0f, 0f);
        }
    }

    private void OnEnable()
    {
        isReleased      = false;                // 풀에서 나올 때 플래그 리셋
        isReplicated    = false;                // 풀에서 나올 때 플래그 리셋
        lastDirection   = transform.forward;    // 초기 방향 설정

        CancelDestruction();                    // 파괴 코루틴 취소
    }

    public void SetTarget(Transform enemyTarget, float bullet_Damage, bool replicated = false)
    {
        target       = enemyTarget;
        damage       = bullet_Damage;
        isReplicated = replicated;
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
        if (isReleased) return;  // 이미 반환 예정이라면 중복 방지

        int otherLayer = other.gameObject.layer;
        if (otherLayer == enemyLayer || otherLayer == bossLayer)
        {
            if (!isReplicated)
            {
                // 로컬 소유자 발사체만 실제 데미지 적용
                if (other.TryGetComponent<Enemy>(out var enemy))
                {
                    enemy.TakeDamage(damage, false);

                    // 15% 확률로 2초 스턴
                    if (UnityEngine.Random.value < StunChance)
                        enemy.Stun(StunDuration);
                }
            }
        }

        isReleased = true;  // 충돌 후에는 더 이상 처리하지 않도록 플래그 설정
        if (Pool != null) Pool.Release(gameObject);
        else Destroy(gameObject);
    }
}
