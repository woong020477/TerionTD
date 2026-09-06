using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>실제 프리팹과 선택 레이캐스트를 사용해 공격 범위가 본체 선택에 섞이는지 검사합니다.</summary>
public static class TowerSelectionValidation
{
    public static void Run()
    {
        if (!Application.isBatchMode)
            throw new InvalidOperationException("Run this validation with unity run in batch mode to preserve open scenes.");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject selectorObject = new GameObject("SelectionValidation");
        SelectManager selector = selectorObject.AddComponent<SelectManager>();
        typeof(SelectManager).GetField("selectableLayerMask", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(selector, ~LayerMask.GetMask("Ignore Raycast"));
        MethodInfo selectHit = typeof(SelectManager).GetMethod("TryGetSelectableHit", BindingFlags.Instance | BindingFlags.NonPublic);
        Camera camera = new GameObject("SelectionValidationCamera").AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 50f;
        camera.pixelRect = new Rect(0f, 0f, 1024f, 768f);
        camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        typeof(SelectManager).GetField("raycastCamera", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(selector, camera);
        int failures = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Tower" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            GameObject tower = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            TowerController controller = tower.GetComponent<TowerController>();
            controller.towerType = prefab.name switch
            {
                "FlamethrowerTower" => TowerType.Flame,
                "LaserTower" => TowerType.Laser,
                "MachineGunTower" => TowerType.Machine,
                "MultipleRocketLauncherTower" => TowerType.Multiple,
                "RocketLauncherTower" => TowerType.Rocket,
                _ => throw new InvalidOperationException($"Unknown tower prefab: {path}")
            };
            typeof(TowerController).GetMethod("EnsureRangeIndicator", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            typeof(TowerController).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            tower.GetComponent<TowerRangeIndicator>().Show();
            Physics.SyncTransforms();
            Bounds body = tower.GetComponent<MeshRenderer>().bounds;
            SphereCollider range = tower.GetComponentInChildren<TowerRangeDetect>(true).GetComponent<SphereCollider>();
            float radius = range.radius * Mathf.Max(range.transform.lossyScale.x, range.transform.lossyScale.y, range.transform.lossyScale.z);
            Debug.Log($"[TowerSelection] {path}: body={body.ToString("F3")}, radius={radius:F3}");
            foreach (Collider collider in tower.GetComponentsInChildren<Collider>(true))
            {
                Debug.Log($"[TowerSelection] collider={collider.name}/{collider.GetType().Name}, trigger={collider.isTrigger}, layer={collider.gameObject.layer}, bounds={collider.bounds.ToString("F3")}");
                if (collider.enabled && collider.GetComponentInParent<ParticleSystem>() != null)
                {
                    Debug.LogError($"[TowerSelection] Unexpected VFX collider attached to {prefab.name}: {collider.name}");
                    failures++;
                }
            }

            int outsideHits = 0;
            int insideHits = 0;
            for (int x = -20; x <= 20; x++)
            for (int z = -20; z <= 20; z++)
            {
                Vector3 point = tower.transform.position + new Vector3(x * radius / 20f, 0f, z * radius / 20f);
                Ray ray = new Ray(new Vector3(point.x, body.max.y + radius + 10f, point.z), Vector3.down);
                object[] args = { ray, default(RaycastHit) };
                bool selected = (bool)selectHit.Invoke(selector, args);
                bool insideBody = point.x >= body.min.x - 0.01f && point.x <= body.max.x + 0.01f
                    && point.z >= body.min.z - 0.01f && point.z <= body.max.z + 0.01f;
                if (selected && !insideBody) outsideHits++;
                if (selected && insideBody) insideHits++;
            }
            Debug.Log($"[TowerSelection] {prefab.name}: outsideBodyHits={outsideHits}, bodyHits={insideHits}");
            if (outsideHits != 0 || insideHits == 0) failures++;

            // 현재 마우스 레이를 범위선 위 빈 공간에 맞춰 실제 클릭 처리의 선택 해제를 검사합니다.
            camera.transform.position = new Vector3(0f, 100f, 0f);
            Ray pointerRay = camera.ScreenPointToRay(Input.mousePosition);
            camera.transform.position += new Vector3(radius - pointerRay.origin.x, 0f, -pointerRay.origin.z);
            typeof(SelectManager).GetField("currentTowerSel", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(selector, controller);
            typeof(SelectManager).GetMethod("HandleClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(selector, null);
            bool rangeStillVisible = tower.GetComponentInChildren<LineRenderer>().enabled;
            Debug.Log($"[TowerSelection] {prefab.name}: rangeVisibleAfterEmptyOutlineClick={rangeStillVisible}");
            if (rangeStillVisible) failures++;
            UnityEngine.Object.DestroyImmediate(tower);
        }
        GameObject explosion = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/VFX/vfx_Explosion_01.prefab");
        foreach (Collider collider in explosion.GetComponentsInChildren<Collider>(true))
            if (collider.enabled) failures++;
        UnityEngine.Object.DestroyImmediate(camera.gameObject);
        UnityEngine.Object.DestroyImmediate(selectorObject);
        if (failures > 0) throw new Exception($"Tower selection validation failed: {failures} prefabs.");
        Debug.Log("[TowerSelection] PASS: all tower bodies selectable; no selection outside body bounds.");
    }
}
