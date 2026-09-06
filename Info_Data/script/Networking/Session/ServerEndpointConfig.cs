using System;
using UnityEngine;

/// <summary>실행 인자·환경 변수·Inspector 순서로 서버 접속 주소와 포트를 결정합니다.</summary>
[Serializable]
public sealed class ServerEndpointConfig
{
    private const string HostEnvironmentKey = "PROJECTMINE_SERVER_HOST";
    private const string TcpPortEnvironmentKey = "PROJECTMINE_TCP_PORT";
    private const string UdpPortEnvironmentKey = "PROJECTMINE_UDP_PORT";
    [SerializeField]
    private string host = "125.185.223.56";
    [SerializeField]
    private int tcpPort = 5000;
    [SerializeField]
    private int udpPort = 9000;
    public string Host => ResolveString("--server-host", HostEnvironmentKey, host);
    public int TcpPort => ResolvePort("--tcp-port", TcpPortEnvironmentKey, tcpPort);
    public int UdpPort => ResolvePort("--udp-port", UdpPortEnvironmentKey, udpPort);

    /// <summary>
    /// 명령줄, 환경 변수, Inspector 기본값 순서로 문자열 설정을 결정합니다.
    /// 이 우선순위 덕분에 빌드를 다시 만들지 않고도 테스트 서버를 교체할 수 있습니다.
    /// </summary>
    private static string ResolveString(string argumentName, string environmentKey, string fallback)
    {
        string argumentValue = FindArgumentValue(argumentName);
        if (!string.IsNullOrWhiteSpace(argumentValue))
            return argumentValue;
        string environmentValue = Environment.GetEnvironmentVariable(environmentKey);
        return string.IsNullOrWhiteSpace(environmentValue) ? fallback : environmentValue;
    }

    /// <summary>
    /// 포트가 유효한 범위인지 검증하고, 잘못된 외부 설정은 안전하게 Inspector 기본값으로 되돌립니다.
    /// </summary>
    private static int ResolvePort(string argumentName, string environmentKey, int fallback)
    {
        string value = ResolveString(argumentName, environmentKey, fallback.ToString());
        return int.TryParse(value, out int port) && port is> 0 and <= 65535 ? port : fallback;
    }

    private static string FindArgumentValue(string argumentName)
    {
        string prefix = argumentName + "=";
        foreach (string argument in Environment.GetCommandLineArgs())
        {
            if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return argument.Substring(prefix.Length);
        }

        return null;
    }
}
