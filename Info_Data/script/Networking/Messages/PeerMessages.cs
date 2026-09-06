using System;

// 연결 제어 계약입니다. 필드 이름은 서버 JSON과 호환되어야 합니다.
// UDP 메시지 베이스 클래스
[System.Serializable]
public class UDPMessageBase
{
    public string action;
}

// UDP 메시지 래퍼 클래스
[Serializable]
public class UdpEnvelope<T>
{
    public string action;
    public T payload;
}

[Serializable]
public struct UdpRoomRegistrationMessage
{
    public int roomId;
    public int userId;
    public bool isHost;
}

[Serializable]
public class UdpPeerListMessage
{
    public int roomId;
    public int hostUserId;
    public UdpPeerInfo[] peers;
}

[Serializable]
public class UdpPeerInfo
{
    public int userId;
    public string address;
    public int port;
    public bool isHost;
}

[Serializable]
public struct UdpHostChangedMessage
{
    public int roomId;
    public int hostUserId;
    public int departedUserId;
}
