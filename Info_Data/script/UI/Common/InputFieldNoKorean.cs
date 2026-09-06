using TMPro;
using UnityEngine;

/// <summary>입력 필드에서 한글 입력을 필터링하는 화면용 보조 컴포넌트입니다.</summary>
public class InputFieldNoKorean : MonoBehaviour
{
    private TMP_InputField pwInputField;
    void Start()
    {
        pwInputField = GetComponent<TMP_InputField>();
    }

    // 비밀번호 입력란 선택 시 IME 모드 끄도록 했는데 효과는 없는거같음..?
    void Update()
    {
        if (pwInputField.isFocused)
        {
            Input.imeCompositionMode = IMECompositionMode.Off;
        }
        else
        {
            Input.imeCompositionMode = IMECompositionMode.Auto;
        }
    }
}
