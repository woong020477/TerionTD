using System.Collections;
using System.Net.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class LoginManager : MonoBehaviour
{
    public static LoginManager Instance; // 싱글톤 인스턴스

    [Header("UI 오브젝트")]
    public GameObject loginUI;                    // 로그인 UI 그룹
    public GameObject forgetEmailandPasswordUI;   // 이메일/비밀번호 찾기 UI 그룹
    public GameObject changePasswordUI;           // 비밀번호 변경 UI 그룹

    // 로그인 관련 UI 요소들
    [Header("로그인 상태 안내용 UI")]
    public TMP_Text loginStatusTextPrefab;              // 로그인 상태 안내용 텍스트
    public Transform statusTextParent;                  // 생성될 부모 오브젝트
    [Header("회원가입 & 로그인 UI")]
    public TMP_InputField usernameInputField;
    public TMP_InputField emailInputField;
    public TMP_InputField passwordInputField;

    [Header("이메일 & 비밀번호 찾기 UI")]
    public TMP_InputField usernameInputField_F;
    public TMP_InputField emailInputField_F;
    public TMP_InputField passwordInputField_F;

    public TMP_Text outputPassword;
    public TMP_Text outputEmail;

    [Header("비밀번호 변경 UI")]
    public TMP_InputField usernameInputField_C;
    public TMP_InputField emailInputField_C;
    public TMP_InputField passwordInputField_C;
    public TMP_InputField ChangePasswordInputField;

    [Header("로그인 UI 버튼 오브젝트")]
    public Button loginButton;
    public Button forgetEmailandPasswordButton;
    public Button signUpButton;
    public Button newAccount;
    public Button changePasswordButton;
    public Button returnToLoginMenuButton;

    [Header("탭 순서 지정")]
    private TMP_InputField[] tabOrderInputFields; // 탭 이동 순서대로 등록할 InputField들

    private void Awake()
    {
        // 싱글톤 인스턴스 설정
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject); // 이미 인스턴스가 존재하면 중복 생성 방지
        }
    }

    void Start()
    {
        // 연결종료 메시지가 비어있지 않다면 1회 출력
        if (!string.IsNullOrEmpty(AuthManager.disconnectMessage))
        {
            PrintStatusText(AuthManager.disconnectMessage, Color.red);
            AuthManager.disconnectMessage = ""; // 한번만 출력되도록 초기화
        }

        // 탭 버튼 클릭 시 이동할 InputField들을 순서대로 배열에 등록
        tabOrderInputFields = new TMP_InputField[]
        {
            usernameInputField,     // 위에서 아래로
            emailInputField,
            passwordInputField,
            passwordInputField_F,   // 왼쪽에서 오른쪽으로
            usernameInputField_F,
            emailInputField_F,
            usernameInputField_C,   // 위에서 아래로
            emailInputField_C,
            passwordInputField_C,
            ChangePasswordInputField
        };
    }


    void Update()
    {
        // 탭 키 입력 처리
        HandleTabNavigation();
    }

    // ---------------- 회원가입 ----------------
    public void OnRegisterClick()   // 회원가입 버튼 클릭 시 호출
    {
        StartCoroutine(AuthManager.Instance.Register());
    }

    // ---------------- 로그인 ----------------
    public void OnLoginClick()  // 로그인 버튼 클릭 시 호출
    {
        StartCoroutine(AuthManager.Instance.Login());
    }

    // ---------------- 비밀번호 재발급 ----------------
    public void OnResetPasswordClick()
    {
        StartCoroutine(AuthManager.Instance.ResetPassword());
    }

    // ---------------- 비밀번호 변경 ----------------
    public void OnChangePasswordClick()
    {
        StartCoroutine(AuthManager.Instance.ChangePassword());
    }

    // ---------------- 이메일 찾기 ----------------
    public void OnFindEmailClick()
    {
        StartCoroutine(AuthManager.Instance.FindEmail());
    }

    // ---------------- UI 전환 기능 ----------------

    // 비밀번호/이메일 찾기 버튼 → 해당 UI 활성화
    public void OnFindEmailPWButtonClick()
    {
        loginUI.SetActive(false);
        forgetEmailandPasswordUI.SetActive(true);
    }

    // 비밀번호 변경 버튼 → 해당 UI 활성화
    public void OnChangePWButtonClick()
    {
        loginUI.SetActive(false);
        changePasswordUI.SetActive(true);
    }

    // 새 계정 생성 버튼 → 회원가입 UI 활성화
    public void OnNewAccountClick()
    {
        loginButton.gameObject.SetActive(false);
        forgetEmailandPasswordButton.gameObject.SetActive(false);
        newAccount.gameObject.SetActive(false);
        changePasswordButton.gameObject.SetActive(false);

        signUpButton.gameObject.SetActive(true);
        usernameInputField.gameObject.SetActive(true);
        returnToLoginMenuButton.gameObject.SetActive(true);
    }

    // 회원가입 UI → 로그인 메뉴로 돌아가기
    public void OnReturnToLoginMenuClick()
    {
        signUpButton.gameObject.SetActive(false);
        usernameInputField.gameObject.SetActive(false);
        returnToLoginMenuButton.gameObject.SetActive(false);

        loginButton.gameObject.SetActive(true);
        forgetEmailandPasswordButton.gameObject.SetActive(true);
        newAccount.gameObject.SetActive(true);
        changePasswordButton.gameObject.SetActive(true);
    }

    // 비밀번호/이메일 찾기 UI → 로그인 메뉴로 돌아가기
    public void OnFReturnToLoginMenuClick()
    {
        loginUI.SetActive(true);
        forgetEmailandPasswordUI.SetActive(false);
    }

    // 비밀번호 변경 UI → 로그인 메뉴로 돌아가기
    public void OnCReturnToLoginMenuClick()
    {
        loginUI.SetActive(true);
        changePasswordUI.SetActive(false);
    }

    // ---------------- 상태 텍스트 출력 기능 ----------------

    /// <summary>
    /// 상태 텍스트를 새로 생성하고 5초 뒤 서서히 사라지게 만드는 함수
    /// </summary>
    /// <param name="text">출력할 문자열</param>
    /// <param name="textColor">텍스트 색상</param>

    public void PrintStatusText(string text, Color textColor)
    {
        // TMP_Text 오브젝트를 복제 생성
        TMP_Text newText = Instantiate(loginStatusTextPrefab, statusTextParent);

        // 내용과 색상 설정
        newText.text = text;
        newText.color = textColor;

        // 오브젝트 활성화
        newText.gameObject.SetActive(true);

        // 사라지기 코루틴 실행
        StartCoroutine(FadeAndDestroyText(newText));
    }

    /// <summary>
    /// 5초 후 서서히 사라지고 파괴되는 코루틴
    /// </summary>
    private IEnumerator FadeAndDestroyText(TMP_Text textObj)
    {
        yield return new WaitForSeconds(5f);

        if (textObj == null)
            yield break;

        while (textObj != null && textObj.color.a > 0)
        {
            var color = textObj.color;
            color.a -= Time.deltaTime;
            textObj.color = color;
            yield return null;
        }

        if (textObj != null)
            Destroy(textObj.gameObject);
    }

    // ---------------- 탭 키 입력 처리 ----------------

    /// <summary>
    /// 탭 키 입력 시 다음 InputField로 포커스를 이동시키는 기능
    /// </summary>
    private void HandleTabNavigation()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            GameObject currentObj = EventSystem.current.currentSelectedGameObject;

            for (int i = 0; i < tabOrderInputFields.Length; i++)
            {
                if (currentObj == tabOrderInputFields[i].gameObject)
                {
                    int nextIndex = (i + 1) % tabOrderInputFields.Length;

                    // 활성화된 필드가 나올 때까지 반복
                    for (int count = 0; count < tabOrderInputFields.Length; count++)
                    {
                        if (tabOrderInputFields[nextIndex].gameObject.activeInHierarchy &&
                            tabOrderInputFields[nextIndex].interactable) // 비활성화 또는 비활성 InputField 건너뛰기
                        {
                            tabOrderInputFields[nextIndex].ActivateInputField();
                            return;
                        }
                        nextIndex = (nextIndex + 1) % tabOrderInputFields.Length;
                    }
                }
            }

            // 현재 선택된 필드가 없으면 첫 번째 활성화된 필드로 이동
            if (tabOrderInputFields.Length > 0)
            {
                for (int i = 0; i < tabOrderInputFields.Length; i++)
                {
                    if (tabOrderInputFields[i].gameObject.activeInHierarchy && tabOrderInputFields[i].interactable)
                    {
                        tabOrderInputFields[i].ActivateInputField();
                        return;
                    }
                }
            }
        }
    }

    // ---------------- 싱글톤 오브젝트 파괴 시 모든 코루틴 종료 ----------------
    private void OnDestroy()
    {
        StopAllCoroutines();
    }
}