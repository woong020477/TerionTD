using System.Collections.Generic;

public enum WaveState
{
    Running,
    Waiting,
    Finished
}

/* ---  제이슨 파일데이터들을 받아올 클래스 --- */
[System.Serializable]
public class WaveData
{
    public int HP;
    public int KillGold;
}

public class WaveStatusWrapper
{
    public List<WaveData> EnemyStatus;
}/*-------------------------------*/

