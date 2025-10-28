[System.Serializable]
public class UpgradeLevel
{
    public int Level;
    public float Rocket;
    public float RocketCost;
    public float Multiple;
    public float MultipleCost;
    public float Machine;
    public float MachineCost;
    public float Laser;
    public float LaserCost;
    public float Flame;
    public float FlameCost;
}

[System.Serializable]
public class UpgradeData
{
    public UpgradeLevel[] TowerUpgrade;
}
