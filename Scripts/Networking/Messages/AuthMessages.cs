using System;
using System.Collections.Generic;

// 서버 응답의 필드 이름과 타입을 유지하는 데이터 계약입니다.
[Serializable]
public class UserData
{
    public string Username;
    public string Email;
    public string PasswordHash;
}

[Serializable]
public class ServerMessage
{
    public string Host;
    public string Command;
    public int RoomId;
    public int UserId;
    public string Username;
    public string Sender;
    public string Message;
    // Title 시스템용 필드
    public int TitleId;
    public string TitleName;
    public string ColorGradient;
    public int DepartedUserId;
}

[Serializable]
public class RoomInfo
{
    public int RoomId;
    public string RoomName;
    public string HostName;
    public string Difficulty;
    public int CurrentPlayers;
    public int MaxPlayers;
    public List<PlayerData> Players;
}
