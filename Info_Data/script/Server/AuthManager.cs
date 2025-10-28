using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class AuthManager : MonoBehaviour
{
    // AuthManager 싱글톤 패턴 적용 및 TCP 클라이언트/스트림, 핑 관련 변수

    /// <summary> 싱글톤 인스턴스 </summary>
    [HideInInspector] public static AuthManager Instance;

    /// <summary> TCP 클라이언트와 스트림을 공유하여 씬 전환 시에도 유지 </summary>
    [HideInInspector] public static TcpClient sharedClient;

    /// <summary> TCP 스트림을 공유하여 씬 전환 시에도 유지 </summary>
    [HideInInspector] public static NetworkStream sharedStream;

    /// <summary> 로그인한 사용자 ID와 사용자명 </summary>
    [HideInInspector] public static int loggedInUserId;

    /// <summary> 로그인한 사용자명 </summary>
    [HideInInspector] public static string loggedInUsername;

    /// <summary> 현재 핑 시간 (ms) </summary>
    [HideInInspector] public static float currentPing = 0f;

    /// <summary> 연결 종료 메시지 </summary>
    [HideInInspector] public static string disconnectMessage;

    /// <summary> 입력받을 타이틀 기본값 세팅 </summary>
    [HideInInspector] public static int loggedInTitleId = 0;
    [HideInInspector] public static string loggedInTitleName = "";
    [HideInInspector] public static string loggedInColorGradient = "#FFFFFF";

    /// <summary>현재 방 정보</summary>
    public RoomInfo CurrentRoomInfo { get; private set; }

    /// <summary>로비 진입 여부</summary>
    public bool LobbyEntered { get; private set; }

    // Timeout 및 Ping관련 변수
    private float timeoutLimit = 300f;              // Timeout 시간(초)
    private bool pingSwitchTrigger = false;         // Ping 플래그
    private float lastPingTime;                     // 마지막 Ping 요청 시간

    private string serverIP = "127.0.0.1";          // TCP 서버 IP(일부러 로컬로 설정해뒀습니다.)
    private int serverPort = 5000;                  // TCP 서버 포트

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);          // 씬 전환 후에도 유지
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        if (pingSwitchTrigger)
        {
            StartCoroutine(PingRoutine());
            pingSwitchTrigger = false;  // 핑 루틴 시작 후 플래그 초기화
        }

        // 서버로부터 메시지 수신 처리
        if (sharedClient != null && sharedClient.Connected && sharedStream != null && sharedStream.DataAvailable)
        {
            byte[] buffer = new byte[2048];
            int bytesRead = sharedStream.Read(buffer, 0, buffer.Length);
            if (bytesRead > 0)
            {
                string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                HandleServerMessage(response); // JsonUtility 기반 처리
            }
        }

        // Timeout 체크 (Ping 응답 시간 기준)
        if (sharedClient != null && sharedClient.Connected && Time.realtimeSinceStartup - lastPingTime > timeoutLimit)
        {
            Debug.LogWarning("Ping 타임아웃");
            HandleDisconnect();
        }
    }

    // ---------------- 이벤트 정의 ----------------
    public static event Action<string, string> OnChatMessageReceived;   // 채팅 메시지 수신 이벤트
    public static event Action<string> OnRoomListReceived;              // 방 목록 수신 이벤트
    public static event Action<string> OnRoomUpdateReceived;            // 방 업데이트 수신 이벤트
    public static event Action<string, string> OnRoomActionResult;      // 방 액션 결과 메시지 처리
    public static event Action<string> OnRoomInfoReceived;              // 방 정보 수신 이벤트

    // ---------------- 서버 메시지 처리 ----------------
    /// <summary>
    /// JSON 문자열을 파싱하여 Command 명령어에 따라 적절한 이벤트를 발생시킵니다.
    /// </summary>
    /// <param name="json">메시지 Json</param>
    /// <returns>성공적으로 처리된 메시지는 OnChatMessageReceived 이벤트를 통해 채팅 메시지를 전달합니다.</returns>
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
                    if (msg == null || string.IsNullOrEmpty(msg.Command)) continue;

                    switch (msg.Command)
                    {
                        case "chat":                            // 채팅 메시지 수신 처리
                            OnChatMessageReceived?.Invoke(packet, packet);
                            break;

                        case "title-update":
                            loggedInTitleId = msg.TitleId;
                            loggedInTitleName = msg.TitleName ?? "";
                            loggedInColorGradient = string.IsNullOrEmpty(msg.ColorGradient) ? "#FFFFFF" : msg.ColorGradient;

                            if(LobbyManager.Instance != null)
                            {
                                LobbyManager.Instance.UpdateTitleUI();
                            }
                            break;

                        case "change-title-result":
                            Debug.Log("칭호 변경 결과: " + msg.Message);
                            break;

                        case "system-chat":                     // 시스템 메시지 수신 처리
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

                        case "lobby-enter":                     // 로비 진입 응답 처리
                            Debug.Log("로비 진입 성공 → " + msg.Message);
                            LobbyEntered = true;                // 로비 진입 상태 업데이트
                            break;

                        case "pong":                            // 핑 응답 처리
                            float currentTime = Time.realtimeSinceStartup;
                            currentPing = (currentTime - lastPingTime) * 1000f;
                            break;

                        case "room-list-update":
                            OnRoomListReceived?.Invoke(packet);
                            break;

                        case "room-update":
                            OnRoomUpdateReceived?.Invoke(packet); // 방 업데이트 메시지 전송
                            break;

                        case "kick-result":
                        case "move-slot-result":
                        case "change-host-result":
                        case "open-slot-result":
                        case "close-slot-result":
                        case "exit-room-result":
                            OnRoomActionResult?.Invoke(msg.Command, msg.Message); // 결과 메시지 전송
                            break;

                        case "room-closed":
                            OnRoomActionResult?.Invoke("room-closed", "방이 호스트에 의해 삭제되었습니다.");
                            break;

                        case "chat-ok":                         // 채팅 메시지 전송 성공 응답 처리
                            Debug.Log("채팅 메시지 전송 성공");
                            break;

                        case "error":                           // 서버 오류 응답 처리
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
                            LobbyManager.Instance.PrintStatusText(msg.Message, Color.green);
                            break;

                        case "room-info":
                            OnRoomInfoReceived?.Invoke(packet); // JSON 그대로 전달
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
                    Debug.LogWarning("단일 메시지 파싱 실패: " + packet + " | " + innerEx.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("서버 메시지 처리 중 오류: " + json + " | " + ex.Message);
        }
    }

    // ---------------- TCP 요청 전송 공통 메서드 ----------------
    /// <summary>
    /// TCP 서버에 JSON 형식의 요청을 보내고 응답을 문자열로 반환합니다.
    /// </summary>
    /// <param name="json">JSON 문자열</param>
    /// <returns></returns>
    private string SendTcpRequest(string json)
    {
        try
        {
            using (TcpClient client = new TcpClient(serverIP, serverPort))
            {
                NetworkStream stream = client.GetStream();
                byte[] data = Encoding.UTF8.GetBytes(json);
                SafeSend(stream, data);  // 데이터 전송 보호

                byte[] buffer = new byte[4096];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                return Encoding.UTF8.GetString(buffer, 0, bytesRead);
            }
        }
        catch (Exception ex)
        {
            return $"서버 오류: {ex.Message}";
        }
    }

    // ---------------- 핑 루틴 ----------------
    /// <summary>
    /// Ping 요청을 주기적으로 서버에 전송하여 연결 상태를 유지하고, 핑 응답 시간을 측정하여 CurrentPing 값을 업데이트합니다.
    /// </summary>
    /// <returns>CurrentPing</returns>
    private IEnumerator PingRoutine()
    {
        while (true)
        {
            if (sharedClient != null && sharedClient.Connected)
            {
                lastPingTime = Time.realtimeSinceStartup;
                string json = "{\"Command\":\"ping\"}";
                byte[] data = Encoding.UTF8.GetBytes(json);
                SafeSend(sharedStream, data);  // 데이터 전송 보호
            }

            yield return new WaitForSeconds(2f); // 2초마다 Ping
        }
    }

    // ---------------- 데이터 전송 보호 ----------------
    /// <summary>
    /// 서버로 데이터를 전송하는 단계를 검사합니다. 연결이 끊어지면 LoginScene으로 자동 전환됩니다.
    /// </summary>
    /// <param name="stream">네트워크 Stream</param>
    /// <param name="data">데이터 byte</param>
    public void SafeSend(NetworkStream stream, byte[] data)
    {
        try
        {
            if (stream != null && stream.CanWrite)
            {
                stream.Write(data, 0, data.Length);
            }
            else
            {
                Debug.LogWarning("스트림이 쓰기 불가 상태입니다.");
                HandleDisconnect();
            }
        }
        catch (SocketException sockEx)
        {
            Debug.LogWarning("소켓 오류 (연결 끊김): " + sockEx.Message);
            HandleDisconnect();   // 로그인 씬으로 복귀
        }
        catch (IOException ioEx)
        {
            Debug.LogWarning("전송 중 오류 발생 (연결 끊김): " + ioEx.Message);
            HandleDisconnect();   // 로그인 씬으로 복귀
        }
        catch (Exception ex)
        {
            Debug.LogWarning("알 수 없는 오류 발생: " + ex.Message);
            HandleDisconnect();   // 로그인 씬으로 복귀
        }
    }

    // ---------------- 회원가입 ----------------
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
        Debug.Log("전송할 JSON: " + jsonData);

        string response = SendTcpRequest(jsonData);
        Debug.Log("응답: " + response);

        var msg = JsonUtility.FromJson<ServerMessage>(response);
        if (msg != null && msg.Command == "register-result")
        {
            // msg.Message: "회원가입이 완료되었습니다."
            LoginManager.Instance.PrintStatusText("회원가입 성공!", Color.green);  // 성공 메시지 출력
            LoginManager.Instance.OnReturnToLoginMenuClick();                      // 로그인 메뉴로 돌아가기
        }
        else
        {
            LoginManager.Instance.PrintStatusText("회원가입 실패: " + response, Color.red);  // 실패 메시지 출력
        }

        yield return null;
    }

    // ---------------- 로그인 ----------------
    /// <summary>
    /// 로그인 화면에서 사용자가 입력한 이메일과 비밀번호를 기반으로 TCP 서버에 로그인 요청을 보냅니다.
    /// </summary>
    /// <returns>성공 시 게임 플레이중이 아니면 로비, 게임 플레이중이면 게임씬로 진입하고, 실패 시 오류 메시지를 출력합니다.</returns>
    public IEnumerator Login()
    {
        try
        {
            // TCP 연결이 안 되어있으면 새로 연결
            if (sharedClient == null || !sharedClient.Connected)
            {
                sharedClient = new TcpClient(serverIP, serverPort);
                sharedStream = sharedClient.GetStream();
            }

            // 로그인 요청 JSON 생성
            string json = $"{{\"Command\":\"login\",\"Email\":\"{LoginManager.Instance.emailInputField.text}\",\"Password\":\"{LoginManager.Instance.passwordInputField.text}\"}}";
            byte[] data = Encoding.UTF8.GetBytes(json);
            SafeSend(sharedStream, data);  // 데이터 전송 보호

            // 서버 응답 수신
            byte[] buffer = new byte[1024];
            int bytesRead = sharedStream.Read(buffer, 0, buffer.Length);
            string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            Debug.Log("서버 응답: " + response);

            // JSON 파싱
            ServerMessage msg = JsonUtility.FromJson<ServerMessage>(response);

            if (msg != null && msg.Command == "login-success")
            {
                loggedInUserId = msg.UserId;
                loggedInUsername = msg.Username;
                loggedInTitleId = msg.TitleId;
                loggedInTitleName = msg.TitleName ?? "";
                loggedInColorGradient = string.IsNullOrEmpty(msg.ColorGradient) ? "#FFFFFF" : msg.ColorGradient;

                pingSwitchTrigger = true;  // 핑 루틴 시작
                // 로비 입장
                EnterLobby();
            }
            else
            {
                StopCoroutine(PingRoutine());
                LoginManager.Instance.PrintStatusText("로그인 실패: " + (msg != null ? msg.Message : response), Color.red);
            }
        }
        catch (Exception ex)
        {
            StopCoroutine(PingRoutine());
            LoginManager.Instance.PrintStatusText("서버 연결 오류: " + ex.Message, Color.red);
        }

        yield return null;
    }

    // ---------------- 로그아웃 요청 ----------------
    /// <summary>로그인한 사용자가 로그아웃 버튼 클릭 시 서버에 로그아웃 요청 후 로그인 씬으로 이동합니다.</summary>
    public void Logout()
    {
        StopCoroutine(PingRoutine());  // 핑 루틴 종료
        string json = $"{{\"Command\":\"logout\",\"UserId\":{loggedInUserId}}}";
        SendToServer(json);
        LoadGameController.Instance.LoadNextScene("LoginScene"); // 로그인 씬으로 전환
    }

    /// <summary>클라이언트 종료 및 시스템 처리로 로그아웃 시 서버에 로그아웃 요청을 합니다.</summary>
    public void QuitGame()
    {
        StopCoroutine(PingRoutine());  // 핑 루틴 종료
        string json = $"{{\"Command\":\"logout\",\"UserId\":{loggedInUserId}}}";
        SendToServer(json);
    }

    // ---------------- 비밀번호 재발급 ---------------
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
        Debug.Log("전송할 JSON: " + jsonData);

        string response = SendTcpRequest(jsonData);
        Debug.Log("응답: " + response);

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

    // ---------------- 비밀번호 변경 ----------------
    /// <summary>
    /// 로그인 화면에서 사용자가 입력한 사용자명, 이메일, 현재 비밀번호, 새 비밀번호를 기반으로 TCP 서버에 비밀번호를 변경 요청을 합니다.
    /// </summary>
    /// <returns>성공 시 "비밀번호 변경 성공" 메시지를 출력하고, 실패 시 오류 메시지를 출력합니다.</returns>
    public IEnumerator ChangePassword()
    {
        if (sharedClient == null || !sharedClient.Connected)
        {
            sharedClient = new TcpClient(serverIP, serverPort);
            sharedStream = sharedClient.GetStream();
        }

        string json = $"{{\"Command\":\"change-password\",\"Username\":\"{LoginManager.Instance.usernameInputField_C.text}\",\"Email\":\"{LoginManager.Instance.emailInputField_C.text}\",\"Password\":\"{LoginManager.Instance.passwordInputField_C.text}\",\"NewPassword\":\"{LoginManager.Instance.ChangePasswordInputField.text}\"}}";

        byte[] data = Encoding.UTF8.GetBytes(json);
        SafeSend(sharedStream, data);  // 데이터 전송 보호

        byte[] buffer = new byte[1024];
        int bytesRead = sharedStream.Read(buffer, 0, buffer.Length);
        string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

        Debug.Log("서버 응답: " + response);

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

    // ---------------- 이메일 찾기 ----------------
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
        Debug.Log("전송할 JSON: " + jsonData);

        string response = SendTcpRequest(jsonData);
        Debug.Log("응답: " + response);

        ServerMessage msg = JsonUtility.FromJson<ServerMessage>(response);
        if (msg.Command == "find-email-result")
        {
            LoginManager.Instance.PrintStatusText("이메일 찾기 성공!", Color.green);
            LoginManager.Instance.outputEmail.text = msg.Message; // 이메일만 출력됨
        }
        else if (msg.Command == "error")
        {
            LoginManager.Instance.PrintStatusText("이메일 찾기 실패: " + msg.Message, Color.red);
        }

        yield return null;
    }

    // ---------------- 로비 진입 ----------------
    /// <summary>로그인 성공 한 UserId, Username 기반으로 TCP서버에 로비로 진입하도록 요청합니다.</summary>
    public void EnterLobby()
    {
        string json = $"{{\"Command\":\"enter-lobby\",\"UserId\":{loggedInUserId},\"Username\":\"{loggedInUsername}\"}}";
        byte[] data = Encoding.UTF8.GetBytes(json);
        SafeSend(sharedStream, data);  // 데이터 전송 보호

        LoadGameController.Instance.LoadNextScene("LobbyScene");
    }

    // ---------------- 방 생성 요청 ----------------
    /// <summary>
    /// 방 생성 요청을 TCP 서버에 전송합니다. 방 이름, 호스트 이름, 난이도를 포함한 JSON 형식으로 데이터를 전송합니다.
    /// </summary>
    /// <returns>성공 시 방 생성</returns>
    public void CreateRoom(string roomName, string hostName, string difficulty)
    {
        string json = $"{{\"Command\":\"create-room\",\"RoomName\":\"{roomName}\",\"HostId\":{AuthManager.loggedInUserId},\"Host\":\"{AuthManager.loggedInUsername}\",\"Difficulty\":\"{difficulty}\"}}";

        byte[] data = Encoding.UTF8.GetBytes(json);
        SafeSend(sharedStream, data);
    }


    // ---------------- 메시지 전송 ----------------
    /// <summary>
    /// 채팅 입력 필드에서 사용자가 입력한 메시지를 JSON 형식으로 변환하여 TCP 서버로 전송합니다.
    /// </summary>
    /// <param name="chatInputField"></param>
    /// <returns>성공적으로 메시지가 전송되면 입력 필드를 비우고 다시 활성화합니다.</returns>
    public void SendUserChatMessage(TMP_InputField chatInputField)
    {
        if (string.IsNullOrWhiteSpace(chatInputField.text)) return;

        string json = $"{{\"Command\":\"chat\",\"UserId\":{loggedInUserId},\"Username\":\"{loggedInUsername}\",\"Message\":\"{chatInputField.text}\"}}";
        byte[] data = Encoding.UTF8.GetBytes(json);
        SafeSend(sharedStream, data);  // 데이터 전송 보호

        chatInputField.text = "";
        chatInputField.ActivateInputField();
    }

    /// <summary>
    /// 시스템이름과 메시지를 JSON 형식으로 변환하여 TCP 서버로 전송합니다.
    /// </summary>
    /// <param name="systemName"></param>
    /// <param name="message"></param>
    /// <returns>성공적으로 메시지가 전송되면 서버에서 시스템 메시지를 출력합니다.</returns>
    public void SendSystemChatMessage(string systemName, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        string json = $"{{\"Command\":\"system-chat\",\"Sender\":\"{systemName}\",\"Message\":\"{message}\"}}";
        byte[] data = Encoding.UTF8.GetBytes(json);
        SafeSend(sharedStream, data);
    }
    
    // ---------------- 서버로 데이터 전송 ----------------
    /// <summary>
    /// 문자열 message를 입력받아 TCP 서버에 문자열 데이터를 전송합니다.
    /// </summary>
    /// <param name="message"></param>
    private void SendToServer(string message)
    {
        try
        {
            // TCP 클라이언트가 연결되어 있고 스트림이 쓰기 가능한지 확인
            if (sharedStream != null && sharedStream.CanWrite)
            {
                byte[] data = Encoding.UTF8.GetBytes(message);  // 메시지를 UTF-8 인코딩으로 변환하여 서버로 전송
                SafeSend(sharedStream, data);       // 스트림에 데이터 쓰기
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[서버] 데이터 전송 실패: " + ex.Message);
        }
    }

    // ---------------- 방 관련 요청 ----------------
    public void RequestRoomInfo(int roomId)
    {
        string json = $"{{\"Command\":\"request-room-info\",\"RoomId\":{roomId}}}";
        byte[] data = Encoding.UTF8.GetBytes(json);
        SafeSend(sharedStream, data);
    }

    public void JoinRoom(int roomId)
    {
        string json = $"{{\"Command\":\"join-room\",\"RoomId\":{roomId},\"UserId\":{loggedInUserId},\"Username\":\"{loggedInUsername}\"}}";
        byte[] data = Encoding.UTF8.GetBytes(json);
        SafeSend(sharedStream, data);
    }

    public void SendKickPlayer(int roomId, int targetUserId)
    {
        string json = $"{{\"Command\":\"kick-player\",\"RoomId\":{roomId},\"TargetUserId\":{targetUserId}}}";
        byte[] data = Encoding.UTF8.GetBytes(json);
        SafeSend(sharedStream, data);
    }

    public void SendMoveSlot(int roomId, int fromSlot, int toSlot)
    {
        string json = $"{{\"Command\":\"move-slot\",\"RoomId\":{roomId},\"FromSlot\":{fromSlot},\"ToSlot\":{toSlot}}}";
        byte[] data = Encoding.UTF8.GetBytes(json);
        SafeSend(sharedStream, data);
    }

    public void SendChangeHost(int roomId, int targetUserId)
    {
        string json = $"{{\"Command\":\"change-host\",\"RoomId\":{roomId},\"TargetUserId\":{targetUserId}}}";
        byte[] data = Encoding.UTF8.GetBytes(json);
        SafeSend(sharedStream, data);
    }

    public void SendOpenSlot(int roomId, int slot)
    {
        string json = $"{{\"Command\":\"open-slot\",\"RoomId\":{roomId},\"Slot\":{slot}}}";
        byte[] data = Encoding.UTF8.GetBytes(json);
        SafeSend(sharedStream, data);
    }

    public void SendCloseSlot(int roomId, int slot)
    {
        string json = $"{{\"Command\":\"close-slot\",\"RoomId\":{roomId},\"Slot\":{slot}}}";
        byte[] data = Encoding.UTF8.GetBytes(json);
        SafeSend(sharedStream, data);
    }

    public void SendKickSlot(int roomId, int targetUserId)
    {
        string json = $"{{\"Command\":\"kick-player\",\"RoomId\":{roomId},\"TargetUserId\":{targetUserId}}}";
        byte[] data = Encoding.UTF8.GetBytes(json);
        SafeSend(sharedStream, data);
    }

    public void SendExitRoom(int roomId)
    {
        string json = $"{{\"Command\":\"exit-room\",\"RoomId\":{roomId},\"UserId\":{loggedInUserId}}}";
        SafeSend(sharedStream, Encoding.UTF8.GetBytes(json));
    }

    public void SendRequestRoomList()
    {
        string json = "{\"Command\":\"request-room-list\"}";
        byte[] data = Encoding.UTF8.GetBytes(json + "\n");
        SafeSend(sharedStream, data);
    }

    // ---------------- 방 정보 요청 ----------------
    private void HandleRoomInfo(string json)
    {
        CurrentRoomInfo = JsonUtility.FromJson<RoomInfo>(json);
        Debug.Log("[서버] 룸 정보 수신 완료");

        LoadGameController.Instance.LoadNextScene("GameScene");
    }

    // ---------------- 연결 종료 처리 ----------------
    /// <summary>연결을 종료하고 로그인 씬으로 이동합니다.</summary>
    private void HandleDisconnect()
    {
        try
        {
            if (sharedStream != null)
            {
                sharedStream.Close();
                sharedStream = null;
            }

            if (sharedClient != null)
            {
                sharedClient.Close();
                sharedClient = null;
            }
            
        }
        catch { }

        disconnectMessage = "서버 연결이 종료되었습니다.";
        StopCoroutine(PingRoutine());  // 핑 루틴 종료
        SceneManager.LoadScene("LoginScene");  // 로그인 씬으로 이동
        Debug.Log("서버 연결 종료 → 로그인 씬으로 이동");
    }

    // ---------------- 싱글톤 오브젝트 파괴 시 모든 코루틴 종료 ----------------
    private void OnDestroy()
    {
        StopAllCoroutines();  // AuthManager가 삭제될 때 코루틴도 종료
    }

    // ---------------- 강제 종료 시 연결 종료 처리 ----------------
    private void OnApplicationQuit()
    {
        QuitGame();
    }
}

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
