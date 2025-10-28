using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct SpawnInfo
{
    public EnemyType type;                  // 생성 할 타입
    public int count;                       // 생성 할 갯수
}

[System.Serializable]
public class Wave
{
    public List<SpawnInfo> spawns;          // 스폰 정보 리스트
}

