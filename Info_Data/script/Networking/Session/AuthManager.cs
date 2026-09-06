using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>로그인 세션과 TCP 응답 이벤트를 관리합니다. 실제 소켓 I/O는 TcpLineConnection에 위임합니다.</summary>
public class AuthManager : MonoBehaviour
{
    // AuthManager 싱글톤 패턴 적용 및 TCP 클라이언트/스트림, 핑 관련 변수
    /// <summary> 싱글톤 인스턴스 </summary>
    [HideInInspector]
    public static AuthManager Instance;
    /// <summary>기존 UI 코드가 연결 여부를 확인할 수 있도록 읽기 전용으로 노출합니다.</summary>
    public static TcpClient sharedClient => Instance?.tcpConnection?.Client;
    /// <summary>신규 코드는 SendServerMessage를 사용하고, 직접 접근은 호환 목적으로만 유지합니다.</summary>
    public static NetworkStream sharedStream => Instance?.tcpConnection?.Stream;

    /// <summary> 로그인한 사용자 ID와 사용자명 </summary>
    [HideInInspector]
    public static int loggedInUserId;
    /// <summary> 로그인한 사용자명 </summary>
    [HideInInspector]
    public static string loggedInUsername;
    /// <summary> 현재 핑 시간 (ms) </summary>
    [HideInInspector]
    public static float currentPing = 0f;
    /// <summary> 연결 종료 메시지 </summary>
    [HideInInspector]
    public static string disconnectMessage;
    /// <summary> 입력받을 타이틀 기본값 세팅 </summary>
    [HideInInspector]
    public static int loggedInTitleId = 0;
    [HideInInspector]
    public static string loggedInTitleName = "";
    [HideInInspector]
    public static string loggedInColorGradient = "#FFFFFF";
    /// <summary>현재 방 정보</summary>
    public RoomInfo CurrentRoomInfo { get; private set; }
    /// <summary>로비 진입 여부</summary>
    public bool LobbyEntered { get; private set; }

    [Header("Server Connection")]
    [SerializeField]
    private ServerEndpointConfig serverEndpoint = new();
    private const float PingIntervalSeconds = 2f;
    private const float ConnectionTimeoutSeconds = 300f;
    private TcpLineConnection tcpConnection;
    private Coroutine pingCoroutine;
    private float lastPingSentTime;
    private float lastPongTime;
    private readonly Queue<bool> roomListRequestNotifications = new();
    public string ServerHost => serverEndpoint.Host;
    public int UdpServerPort => serverEndpoint.UdpPort;

    /// <summary>씬 이동 후에도 인증 세션이 유지되도록 단일 인스턴스와 TCP 전송 객체를 준비합니다.</summary>
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            tcpConnection = new TcpLineConnection();
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 완성된 TCP 패킷을 메인 스레드에서 처리하고 마지막 pong 기준으로 연결 상태를 감시합니다.
    /// </summary>
    private void Update()
    {
        if (tcpConnection != null && tcpConnection.IsConnected)
        {
            try
            {
                foreach (string packet in tcpConnection.ReadAvailableMessages())
                    HandleServerMessage(packet);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("TCP 수신 처리 실패: " + ex.Message);
                HandleDisconnect();
                return;
            }
        }

        if (tcpConnection != null && tcpConnection.IsConnected && Time.realtimeSinceStartup - lastPongTime > ConnectionTimeoutSeconds)
        {
            Debug.LogWarning("Ping 타임아웃");
            HandleDisconnect();
        }
    }

    public static event Action<string, string> OnChatMessageReceived;
    public static event Action<string> OnRoomListReceived;
    public static event Action<string> OnRoomUpdateReceived;
    public static event Action<string, string> OnRoomActionResult;
    public static event Action<string> OnRoomInfoReceived;
    public static event Action<string> OnManualRoomListRefreshCompleted;
    public static event Action<int, int> OnHostChanged; // 새 호스트 UserId, 나간 호스트 UserId
    /// <summary>완성된 서버 응답을 해석해 세션 상태를 반영하고 관련 UI 구독자에게 알립니다.</summary>
    public void HandleServerMessage(string json)
    {
        try
        {
            // 여러 메시지가 '\n'으로 구분되어 올 경우 분리
            string[] packets = json.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string packet in packets)
            {
                try
                {
                    ServerMessage msg = JsonUtility.FromJson<ServerMessage>(packet.Trim());
                    if (msg == null || string.IsNullOrEmpty(msg.Command))
                        continue;
                    switch (msg.Command)
                    {
                        case "chat":
                            OnChatMessageReceived?.Invoke(packet, packet);
                            break;
                        case "title-update":
                            loggedInTitleId = msg.TitleId;
                            loggedInTitleName = msg.TitleName ?? "";
                            loggedInColorGradient = string.IsNullOrEmpty(msg.ColorGradient) ? "#FFFFFF" : msg.ColorGradient;
                            if (LobbyManager.Instance != null)
                            {
                                LobbyManager.Instance.UpdateTitleUI();
                            }

                            break;
                        case "change-title-result":
                            Debug.Log("칭호 변경 결과: " + msg.Message);
                            break;
                        case "system-chat":
                            OnChatMessageReceived?.Invoke(msg.Sender, msg.Message);
                            break;
                        case "login-success":
                            loggedInUserId = msg.UserId;
                            loggedInUsername = msg.Username;
                            loggedInTitleId = msg.TitleId;
                            loggedInTitleName = msg.TitleName ?? "";
                            loggedInColorGradient = string.IsNullOrEmpty(msg.ColorGradient) ? "#FFFFFF" : msg.ColorGradient;
                            Debug.Log($"로그인 성공 (UserId: {msg.UserId}, Username: {msg.Username}, Title: {loggedInTitleName})");
                            break;
                        case "logout-result":
                            Debug.Log("로그아웃 결과: " + msg.Message);
                            break;
                        case "lobby-enter":
                            Debug.Log("로비 진입 성공 → " + msg.Message);
                            LobbyEntered = true;
                            break;
                        case "pong":
                            float currentTime = Time.realtimeSinceStartup;
                            currentPing = (currentTime - lastPingSentTime) * 1000f;
                            lastPongTime = currentTime;
                            break;
                        case "room-list-update":
                            OnRoomListReceived?.Invoke(packet);
                            break;
                        case "room-update":
                            OnRoomUpdateReceived?.Invoke(packet);
                            break;
                        case "kick-result":
                        case "move-slot-result":
                        case "change-host-result":
                        case "open-slot-result":
                        case "close-slot-result":
                        case "exit-room-result":
                        case "close-match-room-result":
                            OnRoomActionResult?.Invoke(msg.Command, msg.Message);
                            break;
                        case "room-closed":
                            LobbyManager.Instance?.HandleRoomClosed(msg.RoomId);
                            break;
                        case "kicked-from-room":
                            ClearCurrentRoomInfo();
                            LobbyManager.Instance?.HandleKickedFromRoom(msg.RoomId, msg.Message);
                            break;
                        case "chat-ok":
                            Debug.Log("채팅 메시지 전송 성공");
                            break;
                        case "error":
                            Debug.LogWarning("서버 오류: " + msg.Message);
                            break;
                        case "create-room-result":
                            string hostName = msg.Host ?? "";
                            string colorGradient = string.IsNullOrEmpty(msg.ColorGradient) ? "#000000" : msg.ColorGradient;
                            Debug.Log($"방 생성 결과: {msg.Message} (Host: {hostName}, Gradient: {colorGradient})");
                            OnRoomActionResult?.Invoke(msg.Command, msg.Message);
                            // 방 생성 성공 시 자동 입장
                            if (msg.RoomId > 0)
                            {
                                JoinRoom(msg.RoomId);
                                LobbyManager.Instance.SetCurrentRoomId(msg.RoomId);
                            }

                            break;
                        case "join-room-result":
                            OnRoomActionResult?.Invoke(msg.Command, msg.Message);
                            if (msg.RoomId > 0)
                            {
                                LobbyManager.Instance.SetCurrentRoomId(msg.RoomId);
                            }

                            break;
                        case "request-room-list-result":
                            bool showRefreshStatus = roomListRequestNotifications.Count > 0 && roomListRequestNotifications.Dequeue();
                            if (showRefreshStatus)
                                OnManualRoomListRefreshCompleted?.Invoke(msg.Message);
                            break;
                        case "room-info":
                            OnRoomInfoReceived?.Invoke(packet);
                            break;
                        case "host-changed":
                            if (CurrentRoomInfo?.Players != null)
                            {
                                foreach (var player in CurrentRoomInfo.Players)
                                    player.IsHost = player.UserId == msg.UserId;
                                CurrentRoomInfo.HostName = msg.Username;
                            }

                            OnHostChanged?.Invoke(msg.UserId, msg.DepartedUserId);
                            UDPClient.Instance?.ApplyHostChange(msg.UserId, msg.DepartedUserId);
                            break;
                        case "room-info-game-start":
                            // RoomInfo 수신 시 GameScene으로 이동
                            Debug.Log("[서버] 게임 시작 전 정보 수집 요청");
                            HandleRoomInfo(packet);
                            break;
                        case "game-start":
                            // 로비 화면이 살아있다면 해당 방 버튼 제거 + 리스트 재요청
                            if (LobbyManager.Instance != null)
                            {
                                LobbyManager.Instance.OnRoomGameStartedBroadcast(msg.RoomId);
                            }

                            break;
                        default:
                            Debug.Log("알 수 없는 명령어 수신: " + msg.Command);
                            break;
                    }
                }
                catch (Exception innerEx)
                {
                    Debug.LogWarning("서버 메시지 파싱 실패: " + innerEx.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("서버 메시지 처리 중 오류: " + ex.Message);
        }
    }

    /// <summary>
    /// 로그인 세션과 분리된 일회성 연결로 요청을 보내고 JSON 응답 한 건을 반환합니다.
    /// 회원가입처럼 지속 세션이 필요 없는 기능이 기존 로그인 연결을 건드리지 않게 합니다.
    /// </summary>
    private string SendTcpRequest(string json)
    {
        try
        {
            return TcpLineConnection.SendRequest(serverEndpoint.Host, serverEndpoint.TcpPort, json);
        }
        catch (Exception ex)
        {
            return $"서버 오류: {ex.Message}";
        }
    }

    /// <summary>
    /// 주기적으로 ping을 보내 마지막 응답 시각과 왕복 시간을 갱신합니다.
    /// 코루틴 참조를 보관해 중복 시작과 종료 누락을 방지합니다.
    /// </summary>
    private IEnumerator PingRoutine()
    {
        while (tcpConnection != null && tcpConnection.IsConnected)
        {
            lastPingSentTime = Time.realtimeSinceStartup;
            SendServerMessage("{\"Command\":\"ping\"}");
            yield return new WaitForSeconds(PingIntervalSeconds);
        }

        pingCoroutine = null;
    }

    private void StartPingRoutine()
    {
        StopPingRoutine();
        lastPingSentTime = Time.realtimeSinceStartup;
        lastPongTime = lastPingSentTime;
        pingCoroutine = StartCoroutine(PingRoutine());
    }

    private void StopPingRoutine()
    {
        if (pingCoroutine == null)
            return;
        StopCoroutine(pingCoroutine);
        pingCoroutine = null;
    }

    /// <summary>
    /// 인증된 지속 연결로 JSON 메시지를 전송합니다.
    /// 네트워크 오류는 한 경로에서 연결 종료 처리해 각 UI가 소켓 예외를 직접 다루지 않게 합니다.
    /// </summary>
    public bool SendServerMessage(string json)
    {
        try
        {
            tcpConnection.Send(json);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("TCP 전송 실패: " + ex.Message);
            HandleDisconnect();
            return false;
        }
    }

    /// <summary>
    /// 로그인 화면에서 사용자가 입력한 사용자명, 비밀번호, 이메일을 기반으로 TCP 서버에 회원가입 요청을 보냅니다.
    /// </summary>
    /// <returns>성공 시 "회원가입 성공" 메시지를 출력하고 로그인 메뉴로 돌아가며, 실패 시 오류 메시지를 출력합니다.</returns>
    public IEnumerator Register()
    {
        // 입력값 가져오기
        var user = new UserData
        {
            Username = LoginManager.Instance.usernameInputField.text,
            PasswordHash = LoginManager.Instance.passwordInputField.text,
            Email = LoginManager.Instance.emailInputField.text
        };
        // JSON 데이터 구성
        string jsonData = $"{{\"Command\":\"register\",\"Username\":\"{user.Username}\",\"Email\":\"{user.Email}\",\"Password\":\"{user.PasswordHash}\"}}";
        string response = SendTcpRequest(jsonData);
        var msg = JsonUtility.FromJson<ServerMessage>(response);
        if (msg != null && msg.Command == "register-result")
        {
            // msg.Message: "회원가입이 완료되었습니다."
            LoginManager.Instance.PrintStatusText("회원가입 성공!", Color.green);
            LoginManager.Instance.OnReturnToLoginMenuClick();
        }
        else
        {
            LoginManager.Instance.PrintStatusText("회원가입 실패: " + response, Color.red);
        }

        yield return null;
    }

    /// <summary>
    /// 로그인 화면에서 사용자가 입력한 이메일과 비밀번호를 기반으로 TCP 서버에 로그인 요청을 보냅니다.
    /// </summary>
    /// <returns>성공 시 게임 플레이중이 아니면 로비, 게임 플레이중이면 게임씬로 진입하고, 실패 시 오류 메시지를 출력합니다.</returns>
    public IEnumerator Login()
    {
        try
        {
            if (!tcpConnection.IsConnected)
                tcpConnection.Connect(serverEndpoint.Host, serverEndpoint.TcpPort);
            string json = $"{{\"Command\":\"login\",\"Email\":\"{LoginManager.Instance.emailInputField.text}\",\"Password\":\"{LoginManager.Instance.passwordInputField.text}\"}}";
            tcpConnection.Send(json);
            string response = tcpConnection.ReadNextMessage();
            ServerMessage msg = JsonUtility.FromJson<ServerMessage>(response);
            if (msg != null && msg.Command == "login-success")
            {
                loggedInUserId = msg.UserId;
                loggedInUsername = msg.Username;
                loggedInTitleId = msg.TitleId;
                loggedInTitleName = msg.TitleName ?? "";
                loggedInColorGradient = string.IsNullOrEmpty(msg.ColorGradient) ? "#FFFFFF" : msg.ColorGradient;
                StartPingRoutine();
                EnterLobby();
            }
            else
            {
                StopPingRoutine();
                LoginManager.Instance.PrintStatusText("로그인 실패: " + (msg != null ? msg.Message : response), Color.red);
            }
        }
        catch (Exception ex)
        {
            StopPingRoutine();
            LoginManager.Instance.PrintStatusText("서버 연결 오류: " + ex.Message, Color.red);
        }

        yield return null;
    }

    /// <summary>로그인한 사용자가 로그아웃 버튼 클릭 시 서버에 로그아웃 요청 후 로그인 씬으로 이동합니다.</summary>
    public void Logout()
    {
        StopPingRoutine();
        roomListRequestNotifications.Clear();
        string json = $"{{\"Command\":\"logout\",\"UserId\":{loggedInUserId}}}";
        SendToServer(json);
        LoadGameController.Instance.LoadNextScene("LoginScene");
    }

    /// <summary>클라이언트 종료 및 시스템 처리로 로그아웃 시 서버에 로그아웃 요청을 합니다.</summary>
    public void QuitGame()
    {
        StopPingRoutine();
        roomListRequestNotifications.Clear();
        string json = $"{{\"Command\":\"logout\",\"UserId\":{loggedInUserId}}}";
        SendToServer(json);
    }

    /// <summary>
    /// 로그인 화면에서 사용자가 입력한 사용자명과 이메일을 기반으로 TCP 서버에 임시 비밀번호를 발급 요청을 합니다.</para>
    /// </summary>
    /// <returns>성공 시 "임시 비밀번호" 메시지를 출력하고, 실패 시 오류 메시지를 출력합니다.</returns>
    public IEnumerator ResetPassword()
    {
        var resetData = new UserData
        {
            Username = LoginManager.Instance.usernameInputField_F.text,
            Email = LoginManager.Instance.emailInputField_F.text
        };
        string jsonData = $"{{\"Command\":\"reset-password\",\"Username\":\"{resetData.Username}\",\"Email\":\"{resetData.Email}\"}}";
        string response = SendTcpRequest(jsonData);
        // JSON 응답 파싱
        ServerMessage msg = JsonUtility.FromJson<ServerMessage>(response);
        if (msg != null && msg.Command == "reset-password-result")
        {
            // UI에 임시 비밀번호 출력
            LoginManager.Instance.outputPassword.text = msg.Message;
            LoginManager.Instance.PrintStatusText("비밀번호 재발급 성공! 하단에 임시 비밀번호를 확인해주세요.", Color.green);
        }
        else if (msg != null && msg.Command == "error")
        {
            LoginManager.Instance.PrintStatusText("비밀번호 재발급 실패: " + msg.Message, Color.red);
        }
        else
        {
            LoginManager.Instance.PrintStatusText("비밀번호 재발급 실패: 알 수 없는 응답", Color.red);
        }

        yield return null;
    }

    /// <summary>
    /// 로그인 화면에서 사용자가 입력한 사용자명, 이메일, 현재 비밀번호, 새 비밀번호를 기반으로 TCP 서버에 비밀번호를 변경 요청을 합니다.
    /// </summary>
    /// <returns>성공 시 "비밀번호 변경 성공" 메시지를 출력하고, 실패 시 오류 메시지를 출력합니다.</returns>
    public IEnumerator ChangePassword()
    {
        if (!tcpConnection.IsConnected)
            tcpConnection.Connect(serverEndpoint.Host, serverEndpoint.TcpPort);
        string json = $"{{\"Command\":\"change-password\",\"Username\":\"{LoginManager.Instance.usernameInputField_C.text}\",\"Email\":\"{LoginManager.Instance.emailInputField_C.text}\",\"Password\":\"{LoginManager.Instance.passwordInputField_C.text}\",\"NewPassword\":\"{LoginManager.Instance.ChangePasswordInputField.text}\"}}";
        tcpConnection.Send(json);
        string response = tcpConnection.ReadNextMessage();
        ServerMessage msg = JsonUtility.FromJson<ServerMessage>(response);
        if (msg.Command == "change-password-result")
        {
            LoginManager.Instance.PrintStatusText(msg.Message, Color.green);
        }
        else if (msg.Command == "error")
        {
            LoginManager.Instance.PrintStatusText("비밀번호 변경 실패: " + msg.Message, Color.red);
        }

        yield return null;
    }

    /// <summary>
    /// 로그인 화면에서 사용자가 입력한 사용자명과 비밀번호를 기반으로 TCP 서버에 이메일을 찾기를 요청합니다.
    /// </summary>
    /// <returns>성공 시 "이메일 찾기 성공" 메시지를 출력하고, 이메일을 표시합니다. 실패 시 오류 메시지를 출력합니다.</returns>
    public IEnumerator FindEmail()
    {
        var findData = new UserData
        {
            Username = LoginManager.Instance.usernameInputField_F.text,
            PasswordHash = LoginManager.Instance.passwordInputField_F.text
        };
        string jsonData = $"{{\"Command\":\"find-email\",\"Username\":\"{findData.Username}\",\"Password\":\"{findData.PasswordHash}\"}}";
        string response = SendTcpRequest(jsonData);
        ServerMessage msg = JsonUtility.FromJson<ServerMessage>(response);
        if (msg.Command == "find-email-result")
        {
            LoginManager.Instance.PrintStatusText("이메일 찾기 성공!", Color.green);
            LoginManager.Instance.outputEmail.text = msg.Message;
        }
        else if (msg.Command == "error")
        {
            LoginManager.Instance.PrintStatusText("이메일 찾기 실패: " + msg.Message, Color.red);
        }

        yield return null;
    }

    /// <summary>로그인 성공 한 UserId, Username 기반으로 TCP서버에 로비로 진입하도록 요청합니다.</summary>
    public void EnterLobby()
    {
        string json = $"{{\"Command\":\"enter-lobby\",\"UserId\":{loggedInUserId},\"Username\":\"{loggedInUsername}\"}}";
        SendServerMessage(json);
        LoadGameController.Instance.LoadNextScene("LobbyScene");
    }

    /// <summary>
    /// 방 생성 요청을 TCP 서버에 전송합니다. 방 이름, 호스트 이름, 난이도를 포함한 JSON 형식으로 데이터를 전송합니다.
    /// </summary>
    /// <returns>성공 시 방 생성</returns>
    public void CreateRoom(string roomName, string hostName, string difficulty)
    {
        string json = $"{{\"Command\":\"create-room\",\"RoomName\":\"{roomName}\",\"HostId\":{AuthManager.loggedInUserId},\"Host\":\"{AuthManager.loggedInUsername}\",\"Difficulty\":\"{difficulty}\"}}";
        SendServerMessage(json);
    }

    /// <summary>
    /// 채팅 입력 필드에서 사용자가 입력한 메시지를 JSON 형식으로 변환하여 TCP 서버로 전송합니다.
    /// </summary>
    /// <param name = "chatInputField"></param>
    /// <returns>성공적으로 메시지가 전송되면 입력 필드를 비우고 다시 활성화합니다.</returns>
    public void SendUserChatMessage(TMP_InputField chatInputField)
    {
        if (string.IsNullOrWhiteSpace(chatInputField.text))
            return;
        string json = $"{{\"Command\":\"chat\",\"UserId\":{loggedInUserId},\"Username\":\"{loggedInUsername}\",\"Message\":\"{chatInputField.text}\"}}";
        SendServerMessage(json);
        chatInputField.text = "";
        chatInputField.ActivateInputField();
    }

    /// <summary>
    /// 시스템이름과 메시지를 JSON 형식으로 변환하여 TCP 서버로 전송합니다.
    /// </summary>
    /// <param name = "systemName"></param>
    /// <param name = "message"></param>
    /// <returns>성공적으로 메시지가 전송되면 서버에서 시스템 메시지를 출력합니다.</returns>
    public void SendSystemChatMessage(string systemName, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        string json = $"{{\"Command\":\"system-chat\",\"Sender\":\"{systemName}\",\"Message\":\"{message}\"}}";
        SendServerMessage(json);
    }

    /// <summary>
    /// 문자열 message를 입력받아 TCP 서버에 문자열 데이터를 전송합니다.
    /// </summary>
    /// <param name = "message"></param>
    private void SendToServer(string message)
    {
        SendServerMessage(message);
    }

    public void RequestRoomInfo(int roomId)
    {
        string json = $"{{\"Command\":\"request-room-info\",\"RoomId\":{roomId}}}";
        SendServerMessage(json);
    }

    public void JoinRoom(int roomId)
    {
        string json = $"{{\"Command\":\"join-room\",\"RoomId\":{roomId},\"UserId\":{loggedInUserId},\"Username\":\"{loggedInUsername}\"}}";
        SendServerMessage(json);
    }

    public void SendKickPlayer(int roomId, int targetUserId)
    {
        string json = $"{{\"Command\":\"kick-player\",\"RoomId\":{roomId},\"TargetUserId\":{targetUserId}}}";
        SendServerMessage(json);
    }

    public void SendMoveSlot(int roomId, int fromSlot, int toSlot)
    {
        string json = $"{{\"Command\":\"move-slot\",\"RoomId\":{roomId},\"FromSlot\":{fromSlot},\"ToSlot\":{toSlot}}}";
        SendServerMessage(json);
    }

    public void SendChangeHost(int roomId, int targetUserId)
    {
        string json = $"{{\"Command\":\"change-host\",\"RoomId\":{roomId},\"TargetUserId\":{targetUserId}}}";
        SendServerMessage(json);
    }

    public void SendOpenSlot(int roomId, int slot)
    {
        string json = $"{{\"Command\":\"open-slot\",\"RoomId\":{roomId},\"Slot\":{slot}}}";
        SendServerMessage(json);
    }

    public void SendCloseSlot(int roomId, int slot)
    {
        string json = $"{{\"Command\":\"close-slot\",\"RoomId\":{roomId},\"Slot\":{slot}}}";
        SendServerMessage(json);
    }

    public void SendKickSlot(int roomId, int targetUserId)
    {
        string json = $"{{\"Command\":\"kick-player\",\"RoomId\":{roomId},\"TargetUserId\":{targetUserId}}}";
        SendServerMessage(json);
    }

    public void SendExitRoom(int roomId)
    {
        string json = $"{{\"Command\":\"exit-room\",\"RoomId\":{roomId},\"UserId\":{loggedInUserId}}}";
        SendServerMessage(json);
    }

    public void SendCloseMatchRoom(int roomId)
    {
        string json = $"{{\"Command\":\"close-match-room\",\"RoomId\":{roomId}}}";
        SendServerMessage(json);
    }

    /// <summary>목록을 요청할 때 수동 갱신 여부를 기록해 자동 갱신 알림과 구분합니다.</summary>
    public void SendRequestRoomList(bool showCompletionStatus = false)
    {
        string json = "{\"Command\":\"request-room-list\"}";
        if (SendServerMessage(json))
            roomListRequestNotifications.Enqueue(showCompletionStatus);
    }

    private void HandleRoomInfo(string json)
    {
        CurrentRoomInfo = JsonUtility.FromJson<RoomInfo>(json);
        Debug.Log("[서버] 룸 정보 수신 완료");
        LoadGameController.Instance.LoadNextScene("GameScene");
    }

    public void ClearCurrentRoomInfo()
    {
        CurrentRoomInfo = null;
    }

    /// <summary>연결 수명주기를 정리하고 로그인 화면으로 돌아갈 이유를 남깁니다.</summary>
    private void HandleDisconnect()
    {
        StopPingRoutine();
        roomListRequestNotifications.Clear();
        tcpConnection?.Disconnect();
        disconnectMessage = "서버 연결이 종료되었습니다.";
        if (SceneManager.GetActiveScene().name != "LoginScene")
            SceneManager.LoadScene("LoginScene");
        Debug.Log("서버 연결 종료 → 로그인 씬으로 이동");
    }

    /// <summary>영속 세션 인스턴스의 소켓과 핑 루틴을 정리합니다.</summary>
    private void OnDestroy()
    {
        if (Instance != this)
            return;
        StopAllCoroutines();
        tcpConnection?.Dispose();
        Instance = null;
    }

    /// <summary>애플리케이션 종료 시 가능한 경우 로그아웃을 알리고 소켓을 즉시 정리합니다.</summary>
    private void OnApplicationQuit()
    {
        StopPingRoutine();
        try
        {
            if (tcpConnection != null && tcpConnection.IsConnected && loggedInUserId > 0)
                tcpConnection.Send($"{{\"Command\":\"logout\",\"UserId\":{loggedInUserId}}}");
        }
        catch
        {
        }
        finally
        {
            tcpConnection?.Disconnect();
        }
    }
}
