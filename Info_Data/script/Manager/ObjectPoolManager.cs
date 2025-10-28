using UnityEngine;
using UnityEngine.Pool;

public class ObjectPoolManager : MonoBehaviour
{

    public static ObjectPoolManager instance;

    public int defaultCapacity = 30;
    public int maxPoolSize = 60;
    public GameObject bulletPrefab;
    public GameObject rocketPrefab;
    public GameObject nuclearRocketPrefab;

    public IObjectPool<GameObject> BulletPool { get; private set; }
    public IObjectPool<GameObject> RocketPool { get; private set; }
    public IObjectPool<GameObject> NuclearRocketPool { get; private set; }

    private void Awake()
    {
        if (instance == null)
            instance = this;
        else
            Destroy(this.gameObject);


        Init();
    }

    private void Init()
    {
        // Bullet 풀 생성
        BulletPool = new ObjectPool<GameObject>(
            CreateBullet,
            OnTakeFromPool,
            OnReturnedToPool,
            OnDestroyPoolObject,
            true,
            defaultCapacity,
            maxPoolSize
        );

        // Rocket 풀 생성
        RocketPool = new ObjectPool<GameObject>(
            CreateRocket,
            OnTakeFromPool,
            OnReturnedToPool,
            OnDestroyPoolObject,
            true,
            defaultCapacity,
            maxPoolSize
        );

        // NuclearRocket 풀 생성
        NuclearRocketPool = new ObjectPool<GameObject>(
            CreateNuclearRocket,
            OnTakeFromPool,
            OnReturnedToPool,
            OnDestroyPoolObject,
            true,
            defaultCapacity,
            maxPoolSize
        );

        // 미리 오브젝트 생성 해놓기
        for (int i = 0; i < defaultCapacity; i++)
        {
            Bullet bullet = CreateBullet().GetComponent<Bullet>();
            bullet.Pool.Release(bullet.gameObject);

            Rocket rocket = CreateRocket().GetComponent<Rocket>();
            rocket.Pool.Release(rocket.gameObject);

            NuclearRocket nuclearRocket = CreateNuclearRocket().GetComponent<NuclearRocket>();
            nuclearRocket.Pool.Release(nuclearRocket.gameObject);
        }
    }

    // Bullet용 생성
    private GameObject CreateBullet()
    {
        GameObject obj = Instantiate(bulletPrefab);
        obj.GetComponent<Bullet>().Pool = BulletPool;
        return obj;
    }

    // Rocket용 생성
    private GameObject CreateRocket()
    {
        GameObject obj = Instantiate(rocketPrefab);
        obj.GetComponent<Rocket>().Pool = RocketPool;
        return obj;
    }

    // NuclearRocket용 생성
    private GameObject CreateNuclearRocket()
    {
        GameObject obj = Instantiate(nuclearRocketPrefab);
        obj.GetComponent<NuclearRocket>().Pool = NuclearRocketPool;
        return obj;
    }

    // 사용
    private void OnTakeFromPool(GameObject poolGo)
    {
        poolGo.SetActive(true);
    }

    // 반환
    private void OnReturnedToPool(GameObject poolGo)
    {
        poolGo.SetActive(false);
    }

    // 삭제
    private void OnDestroyPoolObject(GameObject poolGo)
    {
        Destroy(poolGo);
    }
}