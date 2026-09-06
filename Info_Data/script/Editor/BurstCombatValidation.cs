using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>빈 배치 씬에서 연사 판정/풀 수명주기를 검증합니다. 실제 프레임·두 PC 테스트를 대체하지 않습니다.</summary>
public static class BurstCombatValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int passed;
    private static readonly List<GameObject> objects = new();
    private static readonly List<BurstProjectile> spawned = new();

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use unity run in batch mode.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var randomState = UnityEngine.Random.state;
        try
        {
            RunChecks();
            Debug.Log($"[BurstCombatValidation] PASS: {passed} assertions (isolated engine/iterator checks, not multiplayer).");
        }
        finally
        {
            UnityEngine.Random.state = randomState;
            foreach (var obj in objects) if (obj != null) Object.DestroyImmediate(obj);
            objects.Clear();
            spawned.Clear();
            ObjectPoolManager.instance = null;
            typeof(UDPClient).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
        }
    }

    private static void RunChecks()
    {
        var managerObject = Make("PoolManager");
        managerObject.SetActive(false);
        var manager = managerObject.AddComponent<ObjectPoolManager>();
        ObjectPoolManager.instance = manager;
        Set(manager, "<BulletPool>k__BackingField", MakePool<Bullet>());
        Set(manager, "<RocketPool>k__BackingField", MakePool<Rocket>());
        var tower = Make("Tower").AddComponent<TowerController>();
        tower.bulletSpawnPoint = tower.transform;
        tower.damage = 10f;
        tower.attackDelay = 3f;
        Enemy a = MakeEnemy("TargetA", 1);
        Enemy b = MakeEnemy("TargetB", 2);

        Check(Mathf.Approximately(TowerController.GetBurstDamage(10f), 55f), "ten-shot nominal damage is 55");
        Check(Mathf.Abs(BurstProjectile.BurstStunChance - 0.8031256f) < 0.0001f, "aggregate stun probability preserved");
        Enemy destroyed = MakeEnemy("DestroyedTarget", 3);
        Set(tower, "currentEnemy", destroyed);
        Transform destroyedTransform = destroyed.transform;
        Object.DestroyImmediate(destroyed.gameObject);
        bool oldPathThrows = false;
        try { var ignored = destroyed?.transform; }
        catch (MissingReferenceException) { oldPathThrows = true; }
        Check(oldPathThrows, "original null-conditional expression reproduces destroyed-reference exception");
        tower.ReplicateFire(default, destroyedTransform);
        tower.ReplicateFire(default);
        Check(spawned.Count == 0, "late fire with destroyed/missing target is discarded");
        Set(tower, "currentEnemy", b);
        tower.ReplicateFire(new TowerFireMessage { targetEnemyId = 99, targetSpawnerId = 0 }, a.transform);
        Check(spawned.Count == 0, "mismatched packet target never falls back to current target");
        Set(tower, "lastRemoteBurstSequence", 42);
        Set(tower, "netAimTarget", b.transform);
        tower.ReplicateFire(new TowerFireMessage { towerType = TowerType.Machine, targetEnemyId = a.EnemyId, targetSpawnerId = a.spawnerId, burstSequence = 42, burstShotCount = 10 }, a.transform);
        Check(spawned.Count == 0 && Get<Transform>(tower, "netAimTarget") == b.transform, "duplicate burst does not replay or change aim");

        foreach (TowerType type in new[] { TowerType.Machine, TowerType.Multiple })
        {
            tower.towerType = type;
            Set(tower, "currentEnemy", a);
            spawned.Clear();
            var burst = (IEnumerator)Call(tower, "BurstRoutine");
            Check(burst.MoveNext(), type + " first shot starts");
            Set(tower, "currentEnemy", b);
            for (int shot = 1; shot < 10; shot++) Check(burst.MoveNext(), type + " visual shot " + shot);
            Check(spawned.Count == 10, type + " ten local visuals from one burst");
            int damaging = 0;
            foreach (var projectile in spawned)
            {
                Check(Get<Enemy>(projectile, "target") == a, type + " target remains fixed");
                if (!Get<bool>(projectile, "visualOnly")) damaging++;
            }
            Check(damaging == 1 && Get<float>(spawned[0], "damage") == 55f, type + " one aggregate damage carrier");

            // 정해진 첫 탄환만 명중시킨 뒤 같은 충돌이 재진입해도 피해를 반복하지 않습니다.
            Set(a, "<IsAuthoritative>k__BackingField", true);
            a.HP = 1000;
            AvoidStunForDeterministicCheck();
            Call(spawned[0], "HitTarget");
            Call(spawned[0], "HitTarget");
            for (int i = 1; i < spawned.Count; i++) Call(spawned[i], "HitTarget");
            Check(a.HP == 945 && b.HP == 1000, type + " exactly one damage and no splash to other enemy");
            Check(burst.MoveNext() && !burst.MoveNext(), type + " cooldown completes");

            Set(tower, "currentEnemy", a);
            spawned.Clear();
            burst = (IEnumerator)Call(tower, "BurstRoutine");
            burst.MoveNext();
            a.HP = 0;
            Set(tower, "currentEnemy", b);
            burst.MoveNext(); // 남은 탄 대신 쿨다운으로 전환
            Check(spawned.Count == 1, type + " no remaining shots transferred after target death");
            Call(spawned[0], "Update");
            Check(!spawned[0].gameObject.activeSelf, type + " dead target returns projectile immediately");
            a.HP = 1000;

            spawned.Clear();
            var remote = (IEnumerator)Call(tower, "PlayRemoteBurst", a, new TowerFireMessage { towerType = type }, 10);
            while (remote.MoveNext()) { }
            Check(spawned.Count == 10 && spawned.TrueForAll(p => Get<bool>(p, "visualOnly")), type + " remote burst never applies damage");
            // 배치 EditMode에서는 MonoBehaviour 콜백을 명시적으로 호출해 생명주기 경계를 검사합니다.
            Call(a, "OnDisable");
            Check(spawned.TrueForAll(p => !p.gameObject.activeSelf), type + " target-unavailable event releases every in-flight visual");
        }

        var range = Make("Range").AddComponent<TowerRangeDetect>();
        var targets = Get<HashSet<Enemy>>(range, "inRange");
        targets.Add(a);
        targets.Add(b);
        Set(a, "_isDead", true);
        Check(!range.Contains(a) && range.GetNextTarget(tower.transform) == b, "death-animation target excluded from next burst");

        var slider = Make("HpSlider").AddComponent<Slider>();
        slider.SetValueWithoutNotify(1f);
        Set(b, "hpSlider", slider);
        b.ApplyDamageNetwork(500, 0.9f);
        Check(b.HP == 500 && slider.value == 1f, "authoritative HP immediate, gauge presentation deferred");
        Call(b, "UpdateHpUI", 0f);
        Check(slider.value == 1f, "periodic state refresh preserves gauge animation");
        Set(b, "hpAnimationStarted", Time.time - 1f);
        Call(b, "LateUpdate");
        Check(Mathf.Approximately(slider.value, 0.5f), "gauge reaches authoritative HP");

        var packet = new TowerFireMessage { burstShotCount = 10, burstSequence = 42, damage = 55f };
        var decoded = JsonUtility.FromJson<TowerFireMessage>(JsonUtility.ToJson(packet));
        Check(decoded.burstShotCount == 10 && decoded.burstSequence == 42 && decoded.damage == 55f, "burst packet JSON round trip");
        var hit = JsonUtility.FromJson<EnemyHitRequestMessage>(JsonUtility.ToJson(new EnemyHitRequestMessage { presentationDuration = 0.9f }));
        Check(Mathf.Approximately(hit.presentationDuration, 0.9f), "damage presentation JSON round trip");

        // 기존 MonoScript GUID와 이동 속도 직렬화 값은 베이스 클래스 추출 뒤에도 유지됩니다.
        int prefabCount = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var projectile in prefab.GetComponentsInChildren<BurstProjectile>(true))
            {
                var speed = new SerializedObject(projectile).FindProperty("speed");
                Check(speed != null && speed.floatValue > 0f, "prefab projectile speed preserved: " + prefab.name);
                prefabCount++;
            }
        }
        Check(prefabCount >= 2, "bullet and rocket prefab components load");

        ValidateWireCounts(tower, b);
    }

    private static void ValidateWireCounts(TowerController tower, Enemy target)
    {
        // 운영 서버가 아닌 로컬 UDP 소켓으로 실제 SendUDP 직렬화/전송 횟수를 확인합니다.
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        receiver.Client.ReceiveTimeout = 1000;
        using var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var networkObject = Make("IsolatedUDP");
        networkObject.SetActive(false);
        var network = networkObject.AddComponent<UDPClient>();
        Set(network, "client", sender);
        Set(network, "serverEP", receiver.Client.LocalEndPoint);
        typeof(UDPClient).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, network);

        foreach (var type in new[] { TowerType.Machine, TowerType.Multiple })
        {
            tower.towerType = type;
            target.HP = 1000;
            Set(target, "<IsAuthoritative>k__BackingField", false);
            Set(tower, "currentEnemy", target);
            spawned.Clear();
            var burst = (IEnumerator)Call(tower, "BurstRoutine");
            for (int i = 0; i < 10; i++) burst.MoveNext();
            foreach (var shot in spawned.ToArray()) Call(shot, "HitTarget");
            var endpoint = new IPEndPoint(IPAddress.Any, 0);
            string fire = Encoding.UTF8.GetString(receiver.Receive(ref endpoint));
            string request = Encoding.UTF8.GetString(receiver.Receive(ref endpoint));
            Check(fire.StartsWith("TOWER_FIRE|"), type + " one burst wire packet");
            Check(request.StartsWith("ENEMY_HIT_REQUEST|"), type + " one hit request wire packet");
            Check(receiver.Available == 0 && target.HP == 1000, type + " no per-visual packets or client-side HP mutation");
            var hit = JsonUtility.FromJson<EnemyHitRequestMessage>(request.Substring(request.IndexOf('|') + 1));
            Set(target, "<IsAuthoritative>k__BackingField", true);
            AvoidStunForDeterministicCheck();
            target.ApplyHitRequest(hit.damage, hit.trueDamage, hit.attackerIndex, hit.stunChance, hit.stunDuration, hit.presentationDuration);
            string reply = Encoding.UTF8.GetString(receiver.Receive(ref endpoint));
            Check(reply.StartsWith("ENEMY_DAMAGE|") && target.HP == 945 && receiver.Available == 0, type + " host replies once with aggregate HP");
        }
        Set(network, "client", null);
    }

    private static IObjectPool<GameObject> MakePool<T>() where T : BurstProjectile
    {
        ObjectPool<GameObject> pool = null;
        pool = new ObjectPool<GameObject>(() =>
        {
            var obj = Make(typeof(T).Name);
            obj.SetActive(false);
            var projectile = obj.AddComponent<T>();
            projectile.Pool = pool;
            return obj;
        }, obj =>
        {
            obj.SetActive(true);
            var projectile = obj.GetComponent<T>();
            Call(projectile, "OnEnable");
            spawned.Add(projectile);
        }, obj =>
        {
            Call(obj.GetComponent<T>(), "OnDisable");
            obj.SetActive(false);
        });
        return pool;
    }

    private static Enemy MakeEnemy(string name, int id)
    {
        var enemy = Make(name).AddComponent<Enemy>();
        enemy.EnemyId = id;
        enemy.HP = enemy.MaxHP = 1000;
        return enemy;
    }

    private static GameObject Make(string name)
    {
        var obj = new GameObject(name);
        objects.Add(obj);
        return obj;
    }

    private static void AvoidStunForDeterministicCheck()
    {
        UnityEngine.Random.InitState(123);
        for (;;)
        {
            var state = UnityEngine.Random.state;
            if (UnityEngine.Random.value < BurstProjectile.BurstStunChance) continue;
            UnityEngine.Random.state = state;
            return;
        }
    }

    private static MemberInfo Find(object target, string name, bool method)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            MemberInfo member = method ? (MemberInfo)type.GetMethod(name, Private) : type.GetField(name, Private);
            if (member != null) return member;
        }
        throw new MissingMemberException(target.GetType().Name, name);
    }

    private static object Call(object target, string name, params object[] args) => ((MethodInfo)Find(target, name, true)).Invoke(target, args);
    private static void Set(object target, string name, object value) => ((FieldInfo)Find(target, name, false)).SetValue(target, value);
    private static T Get<T>(object target, string name) => (T)((FieldInfo)Find(target, name, false)).GetValue(target);
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("[BurstCombatValidation] FAIL: " + label);
        passed++;
    }
}
