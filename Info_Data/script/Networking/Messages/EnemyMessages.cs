using System;
using UnityEngine;

// 적의 요청과 호스트 확정 상태를 구분하는 메시지 계약입니다.
// 적 스폰 메시지 구조체들
[System.Serializable]
public struct EnemySpawnMessage
{
    public int enemyId; // 적 ID
    public int spawnerId; // 스포너 ID
    public EnemyType enemyType; // 적 타입
    public long killGold; // 적 처치 시 보상 골드
    public Vector3 position; // 적의 스폰 위치
    public double hp; // 적의 초기 HP
    public float enemyArmor; // 적의 초기 방어력
    public int ownerIndex; // 어떤 플레이어 경로로 이동할 지 결정하는 플레이어 인덱스
}

// 적 데미지 메시지 구조체
[System.Serializable]
public struct EnemyDamageMessage
{
    public int enemyId; // 적 ID
    public int spawnerId; // 스포너 ID
    public float damage; // 데미지 양
    public double remainingHp; // 적의 남은 HP
    public float presentationDuration; // HP 계산과 분리된 게이지 연출 시간
}

[System.Serializable]
public struct EnemyStateMessage
{
    public int enemyId;
    public int spawnerId;
    public Vector3 position;
    public Quaternion rotation;
    public float speed;
    public int routeIndex;
    public EnemyType enemyType;
    public double hp;
    public double maxHp;
    public float armor;
    public long killGold;
}

[System.Serializable]
public struct EnemyStateBatchMessage
{
    public int spawnerId;
    public int activeEnemyCount;
    public EnemyStateMessage[] states;
}

[System.Serializable]
public struct EnemyHitRequestMessage
{
    public int enemyId;
    public int spawnerId;
    public float damage;
    public bool trueDamage;
    public int attackerIndex;
    public float stunChance;
    public float stunDuration;
    public float presentationDuration;
}

[System.Serializable]
public struct EnemyStunRequestMessage
{
    public int enemyId;
    public int spawnerId;
    public float duration;
}

// 적 사망 메시지 구조체
[System.Serializable]
public struct EnemyDeathMessage
{
    public int enemyId; // 적 ID
    public int spawnerId; // 스포너 ID
    public int killerPlayerIndex;
}

[System.Serializable]
public struct EnemyCountMessage
{
    public int spawnerId;
    public int ownerIndex;
    public int count;
}

[System.Serializable]
public struct BossSkillMessage
{
    public int spawnerId;
    public int enemyId;
    public bool start;
    public Vector3 pos;
    public Vector3 fwd;
    public float growDuration;
    public float maxDepth;
    public bool cancelled;
}
