using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>폴더·DTO·카탈로그 분리 후 데이터 조회와 스크립트 참조 및 기존 전투 검사를 함께 실행합니다.</summary>
public static class PortfolioValidation
{
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in batch mode.");
        ValidateFraming();
        ValidateCatalog();
        ValidateScriptReferences();
        BurstCombatValidation.Run();
        TowerSelectionValidation.Run();
        Debug.Log("[PortfolioValidation] PASS: framing, catalog, script references, burst and selection checks.");
    }

    private static void ValidateFraming()
    {
        var buffer = new JsonLineBuffer();
        Require(buffer.Append("{\"Command\":").Count == 0, "partial JSON retained");
        var messages = buffer.Append("\"pong\"}\r\n{\"Command\":\"room-list\"}\n\nunfinished");
        Require(messages.Count == 2 && messages[0] == "{\"Command\":\"pong\"}", "fragmented and coalesced frames");
        buffer.Clear();
        Require(buffer.Append("ok\n")[0] == "ok", "reconnect clears stale data");
        bool overflowRejected = false;
        try { buffer.Append(new string('x', 1024 * 1024 + 1)); }
        catch (InvalidOperationException) { overflowRejected = true; }
        Require(overflowRejected, "bounded buffer rejects overflow");
        Debug.Log("[PortfolioValidation] Framing: 4 checks passed (text boundaries, not UTF-8 byte boundaries).");
    }

    private static void ValidateCatalog()
    {
        var source = Resources.Load<TextAsset>("Data/tower_upgrade_damages_costs");
        Require(source != null, "upgrade resource exists");
        var data = JsonUtility.FromJson<UpgradeData>(source.text);
        int checkedRows = 0;
        foreach (var row in data.TowerUpgrade)
        {
            var loaded = TowerUpgradeCatalog.GetLevel(row.Level);
            Require(loaded != null, "level lookup");
            var damages = new[] { row.Flame, row.Laser, row.Machine, row.Multiple, row.Rocket };
            var costs = new[] { row.FlameCost, row.LaserCost, row.MachineCost, row.MultipleCost, row.RocketCost };
            foreach (TowerType type in Enum.GetValues(typeof(TowerType)))
            {
                TowerStats stats = TowerUpgradeCatalog.GetStats(loaded, type);
                Require(stats.Damage == damages[(int)type] && stats.Cost == costs[(int)type], "resource values unchanged");
                Require(TowerController.StaticGetTowerCost(loaded, type) == stats.Cost, "compatibility cost API");
                Require(TowerController.GetTowerStats(loaded, type).Damage == stats.Damage, "compatibility damage API");
                checkedRows++;
            }
        }
        Debug.Log($"[PortfolioValidation] Catalog: {checkedRows} level/type combinations preserved.");
    }

    private static void ValidateScriptReferences()
    {
        int references = 0;
        foreach (string folder in new[] { "Assets/Scenes", "Assets/Prefabs" })
        foreach (string path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
        {
            if (!path.EndsWith(".unity") && !path.EndsWith(".prefab")) continue;
            foreach (Match match in Regex.Matches(File.ReadAllText(path), @"m_Script: \{fileID: 11500000, guid: ([0-9a-f]{32}), type: 3\}"))
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(match.Groups[1].Value);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                Require(script != null && script.GetClass() != null, "script reference resolves: " + path + " -> " + scriptPath);
                references++;
            }
        }
        Debug.Log($"[PortfolioValidation] Scene/prefab scripts: {references} references resolve.");
    }

    private static void Require(bool condition, string label)
    {
        if (!condition) throw new Exception("[PortfolioValidation] FAIL: " + label);
    }
}
