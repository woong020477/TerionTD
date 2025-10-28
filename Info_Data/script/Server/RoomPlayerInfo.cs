using System.Collections.Generic;

[System.Serializable]
public class RoomInfoData
{
    public int RoomId;
    public List<PlayerInfo> Players;
}

[System.Serializable]
public class PlayerInfo
{
    public int UserId;
    public string Username;
    public int PlayerSlot;
    public bool IsHost;
}
