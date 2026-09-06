using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

/// <summary>
/// 줄바꿈으로 구분된 JSON 패킷을 주고받는 TCP 전송 계층입니다.
/// 인증과 로비 규칙을 알지 않으며 연결, 프레이밍, 동시 전송만 책임집니다.
/// </summary>
public sealed class TcpLineConnection : IDisposable
{
    private readonly byte[] readBuffer = new byte[4096];
    private readonly JsonLineBuffer receiveBuffer = new();
    private readonly Queue<string> pendingMessages = new();
    private readonly object sendLock = new();
    public TcpClient Client { get; private set; }
    public NetworkStream Stream { get; private set; }
    public bool IsConnected => Client != null && Client.Connected && Stream != null;

    /// <summary>
    /// 기존 연결을 정리한 뒤 지정한 엔드포인트에 새 연결을 엽니다.
    /// 재로그인이나 서버 전환 시 이전 스트림이 남지 않도록 항상 한 연결만 소유합니다.
    /// </summary>
    public void Connect(string host, int port)
    {
        Disconnect();
        Client = new TcpClient(host, port);
        Stream = Client.GetStream();
    }

    /// <summary>
    /// JSON 한 건을 전송하고 서버 규약에 맞춰 줄바꿈 구분자를 정확히 한 번 추가합니다.
    /// 여러 시스템이 동시에 보내더라도 패킷 내용이 섞이지 않도록 쓰기 구간을 잠급니다.
    /// </summary>
    public void Send(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        EnsureWritable();
        byte[] payload = Encoding.UTF8.GetBytes(message.TrimEnd('\r', '\n') + "\n");
        lock (sendLock)
        {
            Stream.Write(payload, 0, payload.Length);
        }
    }

    /// <summary>
    /// 현재 도착해 있는 TCP 조각을 읽고 완성된 JSON 줄만 반환합니다.
    /// 불완전한 마지막 조각은 다음 프레임까지 내부 버퍼에 보존합니다.
    /// </summary>
    public IReadOnlyList<string> ReadAvailableMessages()
    {
        if (!IsConnected)
            return Array.Empty<string>();
        while (Stream.DataAvailable)
        {
            int bytesRead = Stream.Read(readBuffer, 0, readBuffer.Length);
            if (bytesRead == 0)
                throw new InvalidOperationException("서버가 TCP 연결을 종료했습니다.");
            EnqueueChunk(bytesRead);
        }

        if (pendingMessages.Count == 0)
            return Array.Empty<string>();
        var messages = new List<string>(pendingMessages.Count);
        while (pendingMessages.Count > 0)
            messages.Add(pendingMessages.Dequeue());
        return messages;
    }

    /// <summary>
    /// 로그인처럼 즉시 응답이 필요한 요청에서 JSON 한 줄이 완성될 때까지 기다립니다.
    /// 같은 수신 버퍼를 재사용하므로 응답 뒤에 함께 도착한 메시지도 잃지 않습니다.
    /// </summary>
    public string ReadNextMessage()
    {
        EnsureReadable();
        while (pendingMessages.Count == 0)
        {
            int bytesRead = Stream.Read(readBuffer, 0, readBuffer.Length);
            if (bytesRead == 0)
                throw new InvalidOperationException("응답을 기다리는 중 서버 연결이 종료되었습니다.");
            EnqueueChunk(bytesRead);
        }

        return pendingMessages.Dequeue();
    }

    /// <summary>
    /// 일회성 연결로 요청과 응답을 처리합니다.
    /// 회원가입처럼 로그인 세션과 독립적인 요청이 지속 연결 상태를 오염시키지 않게 합니다.
    /// </summary>
    public static string SendRequest(string host, int port, string message)
    {
        using var connection = new TcpLineConnection();
        connection.Connect(host, port);
        connection.Send(message);
        return connection.ReadNextMessage();
    }

    /// <summary>소켓과 누적 패킷을 함께 정리해 다음 연결이 이전 상태를 재사용하지 않도록 합니다.</summary>
    public void Disconnect()
    {
        try
        {
            Stream?.Close();
        }
        catch
        {
        }

        try
        {
            Client?.Close();
        }
        catch
        {
        }

        Stream = null;
        Client = null;
        receiveBuffer.Clear();
        pendingMessages.Clear();
    }

    public void Dispose()
    {
        Disconnect();
    }

    private void EnqueueChunk(int bytesRead)
    {
        string chunk = Encoding.UTF8.GetString(readBuffer, 0, bytesRead);
        foreach (string message in receiveBuffer.Append(chunk))
            pendingMessages.Enqueue(message);
    }

    private void EnsureWritable()
    {
        if (!IsConnected || !Stream.CanWrite)
            throw new InvalidOperationException("TCP 연결이 없거나 스트림에 쓸 수 없습니다.");
    }

    private void EnsureReadable()
    {
        if (!IsConnected || !Stream.CanRead)
            throw new InvalidOperationException("TCP 연결이 없거나 스트림을 읽을 수 없습니다.");
    }
}
