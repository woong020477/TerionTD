using UnityEngine;

/// <summary>타워 업그레이드 JSON을 한 번 읽고 레벨별 피해·비용을 조회합니다. 전투와 UI 상태는 소유하지 않습니다.</summary>
public static class TowerUpgradeCatalog
{
    private static UpgradeData upgradeData;
    private static bool isDataLoaded;
    public static void Load()
    {
        if (isDataLoaded && upgradeData != null)
            return;
        // Resources/Data/tower_upgrade_damages_costs.json 로 넣어두기 (TextAsset)
        var ta = Resources.Load<TextAsset>("Data/tower_upgrade_damages_costs");
        if (ta == null)
        {
            Debug.LogError("업그레이드 JSON을 Resources에 넣으세요: Resources/Data/tower_upgrade_damages_costs.json");
            return;
        }

        upgradeData = JsonUtility.FromJson<UpgradeData>(ta.text);
        if (upgradeData?.TowerUpgrade != null)
        {
            isDataLoaded = true;
        }
        else
        {
            Debug.LogError("[TowerUpgradeCatalog] 업그레이드 JSON 파싱 실패");
        }
    }

    // 업그레이드 데이터를 가져오는 메소드
    public static UpgradeLevel GetLevel(int level)
    {
        Load();
        if (upgradeData?.TowerUpgrade == null)
            return null;
        foreach (var data in upgradeData.TowerUpgrade)
        {
            if (data.Level == level)
                return data;
        }

        return null;
    }

    // 타워 비용을 가져오는 메소드
    public static float GetCost(UpgradeLevel data, TowerType towerType)
    {
        switch (towerType)
        {
            case TowerType.Rocket:
                return data.RocketCost;
            case TowerType.Multiple:
                return data.MultipleCost;
            case TowerType.Machine:
                return data.MachineCost;
            case TowerType.Laser:
                return data.LaserCost;
            case TowerType.Flame:
                return data.FlameCost;
            default:
                return 0;
        }
    }

    // 타워의 공격력과 비용을 가져오는 메소드
    public static TowerStats GetStats(UpgradeLevel data, TowerType towerType)
    {
        TowerStats stats = new TowerStats();
        switch (towerType)
        {
            case TowerType.Rocket:
                stats.Damage = data.Rocket;
                stats.Cost = data.RocketCost;
                break;
            case TowerType.Multiple:
                stats.Damage = data.Multiple;
                stats.Cost = data.MultipleCost;
                break;
            case TowerType.Machine:
                stats.Damage = data.Machine;
                stats.Cost = data.MachineCost;
                break;
            case TowerType.Laser:
                stats.Damage = data.Laser;
                stats.Cost = data.LaserCost;
                break;
            case TowerType.Flame:
                stats.Damage = data.Flame;
                stats.Cost = data.FlameCost;
                break;
            default:
                stats.Damage = 0;
                stats.Cost = 0;
                break;
        }

        return stats;
    }
}
