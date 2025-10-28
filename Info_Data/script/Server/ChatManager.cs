using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ChatManager : MonoBehaviour
{
    [Header("UI 연결 요소")]
    [SerializeField] private TMP_InputField chatInputField; // 채팅 입력창
    [SerializeField] private Button sendButton;             // 전송 버튼
    [SerializeField] private ScrollRect chatScrollRect;     // 스크롤 영역
    [SerializeField] private Transform chatContent;         // 메시지들이 붙는 Content
    [SerializeField] private GameObject chatMessagePrefab;  // 메시지 Prefab
    [SerializeField] private TMP_Dropdown chatTypeDropdown; // 채팅 타입 드롭다운
    private void Start()
    {
        
    }

    // ---------------- 메시지 전송 ----------------
    public void SendMessage()
    {
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
        if (chatInputField == null || chatTypeDropdown == null)
        {
            Debug.LogError("ChatManager의 InputField 또는 Dropdown이 연결되지 않았습니다.");
            return;
        }

        if (AuthManager.sharedStream == null)
        {
            Debug.LogWarning("서버와 연결되지 않아 메시지를 보낼 수 없습니다.");
            return;
        }

        if (string.IsNullOrWhiteSpace(chatInputField.text)) return;

        // 채팅 타입과 메시지 텍스트 가져오기
        string chatType = chatTypeDropdown.options[chatTypeDropdown.value].text;
        string targetUser = "";
        string messageText = chatInputField.text;

        // Whisper 구문 처리
        if (chatType == "Whisper" && messageText.StartsWith("/"))
        {
            int spaceIdx = messageText.IndexOf(' ');
            if (spaceIdx > 1)
            {
                targetUser = messageText.Substring(1, spaceIdx - 1);
                messageText = messageText.Substring(spaceIdx + 1);
            }
        }

        // 서버 관리 명령어 감지
        if (messageText.StartsWith("/칭호"))
        {
            string[] parts = messageText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3 && int.TryParse(parts[1], out int targetId) && int.TryParse(parts[2], out int newTitleId))
            {
                string titleJson = $"{{\"Command\":\"change-title\",\"UserId\":{targetId},\"TitleId\":{newTitleId}}}";
                byte[] titleData = Encoding.UTF8.GetBytes(titleJson + "\n");
                AuthManager.Instance.SafeSend(AuthManager.sharedStream, titleData);
                chatInputField.text = "";
                return;
            }
        }

        // 일반채팅 메시지 처리
        ChatRequest req = new ChatRequest
        {
            Command = "chat",
            UserId = AuthManager.loggedInUserId,
            Username = AuthManager.loggedInUsername,
            Message = messageText,
            ChatType = chatType,
            TargetUsername = targetUser
        };

        string json = JsonUtility.ToJson(req);
        byte[] data = Encoding.UTF8.GetBytes(json + "\n");

        AuthManager.Instance.SafeSend(AuthManager.sharedStream, data);


        chatInputField.text = "";
        chatInputField.ActivateInputField();
    }


    private void OnEnable()             // 씬 활성 시 이벤트 구독
    {
        // AuthManager에서 채팅 이벤트 구독
        AuthManager.OnChatMessageReceived += HandleIncomingLobbyChat;
    }

    private void OnDisable()            // 씬 비활성 시 이벤트 구독 해제
    {
        // 씬 전환 시 이벤트 중복 방지
        AuthManager.OnChatMessageReceived -= HandleIncomingLobbyChat;
    }

    // ---------------- 채팅 메시지 수신 처리 ----------------
    private void HandleIncomingLobbyChat(string sender, string message)
    {
        var data = JsonUtility.FromJson<ChatPacket>(message);

        if (data == null)
        {
            Debug.LogWarning("채팅 패킷 파싱 실패");
            return;
        }

        // 채팅 타입에 따라 색상 태그 설정
        string colorTag = "#FFFFFF";
        if (data.ChatType == "Room") colorTag = "#83ADFF";
        else if (data.ChatType == "Whisper") colorTag = "#9E83FF";

        // 채팅 메시지 표시 형식 설정
        string displayMsg = $"<color={colorTag}>[{data.ChatType}]</color> ";

        // 칭호 적용
        if (!string.IsNullOrEmpty(data.TitleName))
        {
            displayMsg += $"<gradient={data.ColorGradient}>[{data.TitleName}]{data.Sender}</gradient>";
        }
        else
        {
            displayMsg += $"{data.Sender}";
        }
        displayMsg += $" : {data.Message}";

        AddMessageToUI(displayMsg);
    }

// ---------------- UI에 메시지 추가 ----------------
private void AddMessageToUI(string message)
    {
        GameObject newMsg = Instantiate(chatMessagePrefab, chatContent);
        TMP_Text msgText = newMsg.GetComponent<TMP_Text>();
        msgText.text = message;

        // 자동 스크롤
        Canvas.ForceUpdateCanvases();
        chatScrollRect.verticalNormalizedPosition = 0f;
    }
}

[System.Serializable]
public class ChatPacket
{
    public string Sender;
    public string Message;
    public string ChatType;
    public string Target;
    public string TitleName;
    public string ColorGradient;
}

[System.Serializable]
public class ChatRequest
{
    public string Command = "chat";
    public int UserId;
    public string Username;
    public string Message;
    public string ChatType;
    public string TargetUsername; // Whisper 용
}
