using System;

// 플레이어 라인별 웨이브와 결과를 전달하는 메시지 계약입니다.
// 게임 시작 메시지 구조체
[System.Serializable]
public struct GameStartMessage
{
    public double startUnix; // UTC unix seconds
}

// 게임 시간 스케일 메시지 구조체
[System.Serializable]
public struct GameTimeScaleMessage
{
    public float scale; // 현재 시간 스케일
}

// 웨이브 시작 메시지 구조체
[Serializable]
public struct WaveStartMessage
{
    public int spawnerId; // 스포너 ID
    public int ownerIndex; // 웨이브 시작한 플레이어 인덱스
    public int waveIndex; // 웨이브 인덱스
    public double hp; // 웨이브 시작 시 적의 초기 HP
    public long killGold; // 웨이브 시작 시 적 처치 보상 골드
}

// 웨이브 클리어 메시지 구조체
[Serializable]
public struct WaveClearMessage
{
    public int spawnerId; // 스포너 ID
    public int ownerIndex; // 웨이브 클리어한 플레이어 인덱스
    public bool isFinalWave;
}

[Serializable]
public struct PlayerMatchResultMessage
{
    public int ownerIndex;
    public MatchResult result;
}

[Serializable]
public struct MatchCompleteMessage
{
    public PlayerMatchResultMessage[] results;
}
