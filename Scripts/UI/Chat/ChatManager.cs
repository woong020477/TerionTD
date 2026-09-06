using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>채팅 수신 이벤트를 현재 화면에 표시하고 전송 입력을 처리합니다.</summary>
public class ChatManager : MonoBehaviour
{
    [Header("UI 연결 요소")]
    [SerializeField]
    private TMP_InputField chatInputField;
    [SerializeField]
    private Button sendButton;
    [SerializeField]
    private ScrollRect chatScrollRect;
    [SerializeField]
    private Transform chatContent;
    [SerializeField]
    private GameObject chatMessagePrefab;
    [SerializeField]
    private TMP_Dropdown chatTypeDropdown;
    /// <summary>
    /// 입력 내용을 채팅 요청으로 변환해 서버에 전송합니다.
    /// 개발용 칭호 변경은 '/칭호 칭호ID' 형식이며 현재 로그인한 사용자에게만 적용됩니다.
    /// </summary>
    public void SendMessage()
    {
        SoundManager.Instance.PlaySFX(SoundKey.UI_Click);
        if (!ValidateRequiredReferences() || string.IsNullOrWhiteSpace(chatInputField.text))
            return;
        string chatType = chatTypeDropdown.options[chatTypeDropdown.value].text;
        string targetUser = string.Empty;
        string messageText = chatInputField.text.Trim();
        if (chatType == "Whisper")
            TryParseWhisper(ref targetUser, ref messageText);
        if (TrySendTitleCommand(messageText))
        {
            ClearInput();
            return;
        }

        var request = new ChatRequest
        {
            UserId = AuthManager.loggedInUserId,
            Username = AuthManager.loggedInUsername,
            Message = messageText,
            ChatType = chatType,
            TargetUsername = targetUser
        };
        if (AuthManager.Instance.SendServerMessage(JsonUtility.ToJson(request)))
            ClearInput();
    }

    /// <summary>활성화된 동안만 채팅 이벤트를 구독해 씬 재진입 시 중복 출력되지 않게 합니다.</summary>
    private void OnEnable()
    {
        AuthManager.OnChatMessageReceived += HandleIncomingLobbyChat;
    }

    private void OnDisable()
    {
        AuthManager.OnChatMessageReceived -= HandleIncomingLobbyChat;
    }

    /// <summary>
    /// 서버 패킷을 화면 표시 문자열로 변환합니다.
    /// 채팅 종류와 칭호 색상은 표현 계층에서만 적용하고 원본 데이터는 변경하지 않습니다.
    /// </summary>
    private void HandleIncomingLobbyChat(string sender, string message)
    {
        ChatPacket data = JsonUtility.FromJson<ChatPacket>(message);
        if (data == null)
        {
            Debug.LogWarning("채팅 패킷 파싱 실패");
            return;
        }

        string colorTag = data.ChatType switch
        {
            "Room" => "#83ADFF",
            "Whisper" => "#9E83FF",
            _ => "#FFFFFF"
        };
        string senderLabel = string.IsNullOrEmpty(data.TitleName) ? data.Sender : $"<gradient={data.ColorGradient}>[{data.TitleName}]{data.Sender}</gradient>";
        AddMessageToUI($"<color={colorTag}>[{data.ChatType}]</color> {senderLabel} : {data.Message}");
    }

    /// <summary>메시지 프리팹을 추가하고 새 메시지가 보이도록 스크롤을 맨 아래로 이동합니다.</summary>
    private void AddMessageToUI(string message)
    {
        GameObject newMessage = Instantiate(chatMessagePrefab, chatContent);
        newMessage.GetComponent<TMP_Text>().text = message;
        Canvas.ForceUpdateCanvases();
        chatScrollRect.verticalNormalizedPosition = 0f;
    }

    private bool ValidateRequiredReferences()
    {
        if (chatInputField == null || chatTypeDropdown == null || chatMessagePrefab == null || chatContent == null || chatScrollRect == null)
        {
            Debug.LogError("ChatManager의 필수 UI 참조가 연결되지 않았습니다.");
            return false;
        }

        if (AuthManager.Instance == null || AuthManager.sharedStream == null)
        {
            Debug.LogWarning("서버와 연결되지 않아 메시지를 보낼 수 없습니다.");
            return false;
        }

        return true;
    }

    private static void TryParseWhisper(ref string targetUser, ref string messageText)
    {
        if (!messageText.StartsWith("/", StringComparison.Ordinal))
            return;
        int spaceIndex = messageText.IndexOf(' ');
        if (spaceIndex <= 1)
            return;
        targetUser = messageText.Substring(1, spaceIndex - 1);
        messageText = messageText.Substring(spaceIndex + 1);
    }

    private static bool TrySendTitleCommand(string messageText)
    {
        if (!messageText.StartsWith("/칭호", StringComparison.Ordinal))
            return false;
        string[] parts = messageText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out int newTitleId))
            return false;
        return AuthManager.Instance.SendServerMessage($"{{\"Command\":\"change-title\",\"UserId\":{AuthManager.loggedInUserId},\"TitleId\":{newTitleId}}}");
    }

    private void ClearInput()
    {
        chatInputField.text = string.Empty;
        chatInputField.ActivateInputField();
    }
}

[Serializable]
public class ChatPacket
{
    public string Sender;
    public string Message;
    public string ChatType;
    public string Target;
    public string TitleName;
    public string ColorGradient;
}

[Serializable]
public class ChatRequest
{
    public string Command = "chat";
    public int UserId;
    public string Username;
    public string Message;
    public string ChatType;
    public string TargetUsername;
}
