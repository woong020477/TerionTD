using System.Collections.Generic;

// 서버 응답의 필드 이름과 타입을 유지하는 데이터 계약입니다.
[System.Serializable]
public class RoomUpdateData
{
    public string RoomName;
    public int RoomId;
    public string Host;
    public string Difficulty;
    public string ColorGradient;
    public PlayerData[] Players;
}

[System.Serializable]
public class PlayerData
{
    public int PlayerSlot;
    public int UserId;
    public string Username;
    public bool IsHost;
    public bool IsClosed;
    public int TitleId;
    public string TitleName;
    public string ColorGradient;
}

[System.Serializable]
public class RoomListWrapper
{
    public List<RoomData> Rooms;
}

[System.Serializable]
public class RoomData
{
    public int RoomId;
    public string RoomName;
    public string Host;
    public string Difficulty;
    public int CurrentPlayers;
    public int MaxPlayers;
    public string ColorGradient;
}
