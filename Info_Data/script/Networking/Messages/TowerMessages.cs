using System;
using UnityEngine;

// 소유자·로컬 ID로 타워를 구분하는 게임플레이 메시지입니다.
// 타워베이스 건설 메시지 구조체
[System.Serializable]
public struct TowerBasePlaceMessage
{
    public int ownerIndex; // 플레이어 인덱스
    public int baseNetId; // 베이스 네트워크 ID
    public Vector3 position; // 건설 위치
}

// 타워 건설 메시지 구조체
public struct TowerCreateMessage
{
    public int ownerIndex; // 플레이어 인덱스
    public int baseNetId; // 베이스 네트워크 ID
    public TowerType towerType; // 타워 타입
    public int level; // 타워 레벨
}

// 타워 업그레이드 메시지 구조체
[System.Serializable]
public struct TowerUpgradeMessage
{
    public int baseNetId; // 베이스 네트워크 ID
    public TowerType towerType; // 타워 타입
    public int level; // 타워 레벨
}

// 타워 연구 메시지 구조체
[System.Serializable]
public struct TowerLabUpgradeMessage
{
    public int ownerIndex; // 연구한 플레이어
    public TowerType towerType; // 연구한 타워 타입
    public int level; // 새 연구 레벨
}

// 타워 발사 메시지 구조체
[System.Serializable]
public struct TowerFireMessage
{
    public int ownerIndex; // 타워 소유 플레이어
    public int towerId; // 타워 식별 ID
    public TowerType towerType; // Flame, Laser 등등
    public Vector3 position; // 발사 위치
    public float damage; // 공격력
    public bool isFirstShot; // 머신건·멀티 로켓 등 첫발 여부
    public int targetEnemyId; // 타겟 적 ID
    public int targetSpawnerId;
    public int burstShotCount; // 한 패킷으로 재현할 연사 수. 0은 구 버전 단발입니다.
    public int burstSequence; // 동일 버스트의 중복/역순 수신 방지
}

// 타워 이동 메시지 구조체
[System.Serializable]
public struct TowerMoveMessage
{
    public int ownerIndex; // 이동 요청자
    public int baseNetId; // 베이스 네트워크 ID
    public Vector3 basePosition; // 베이스 최종 위치
    public bool hasTower; // 베이스에 타워가 있었는가
    public Vector3 towerPosition; // 타워 최종 위치(있을 때만 유효)
}
