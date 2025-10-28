using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Audio;

[System.Serializable]
public struct ResOption
{
    public int width;
    public int height;
    public bool windowedOnly; // true면 창모드만 허용
    public ResOption(int w, int h, bool winOnly) { width = w; height = h; windowedOnly = winOnly; }
    public override string ToString()
    {
        return windowedOnly ? $"{width} x {height} (창모드)" : $"{width} x {height}";
    }
}

public class OptionManager : MonoBehaviour
{
    public static OptionManager Instance { get; private set; }

    [Header("Canvas & 그룹")]
    [SerializeField] private CanvasGroup canvasGroup;       // 열기/닫기 시 α 즉시 변경 대상

    [Header("사운드 UI")]
    [SerializeField] private Slider bgmSlider;              // 0.0~1.0
    [SerializeField] private Slider sfxSlider;              // 0.0~1.0

    [Header("해상도/전체화면 UI")]
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private Toggle fullscreenToggle;

    [Header("AudioMixer")]
    public AudioMixer audioMixer;
    [SerializeField] private string bgmParam = "BGM";       // AudioMixer 노출 파라미터명
    [SerializeField] private string sfxParam = "SFX";       // AudioMixer 노출 파라미터명

    private readonly List<ResOption> _resList = new()
    {
        new ResOption(1280, 720, true),
        new ResOption(1366, 768, true),
        new ResOption(1920,1080, false),
        new ResOption(2560,1440, false),
    };

    private bool _isOpen;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 안전장치: CanvasGroup이 없으면 붙여줌
        if (!canvasGroup)
        {
            canvasGroup = gameObject.GetComponent<CanvasGroup>();
            if (!canvasGroup) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        // 시작 시 비활성 상태(α=0)
        SetCanvasVisible(false, immediate: true);
    }

    private void Start()
    {
        // 드롭다운 채우기
        resolutionDropdown.ClearOptions();
        var options = new List<string>();
        foreach (var r in _resList) options.Add(r.ToString());
        resolutionDropdown.AddOptions(options);

        // UI 리스너
        resolutionDropdown.onValueChanged.AddListener(OnResolutionDropdownChanged);
        fullscreenToggle.onValueChanged.AddListener(OnFullscreenToggleChanged);
        bgmSlider.onValueChanged.AddListener(OnBGMSliderChanged);
        sfxSlider.onValueChanged.AddListener(OnSFXSliderChanged);

        // 데이터 로드 및 UI/시스템 반영
        ApplyLoadedDataToUI();
        ApplySettingsToSystem();
    }

    // -------- UI <-> 데이터 --------

    private void ApplyLoadedDataToUI()
    {
        var data = OptionDataManager.Instance.Data;

        // 방어: 인덱스 범위 강제
        data.resolutionIndex = Mathf.Clamp(data.resolutionIndex, 0, _resList.Count - 1);

        // 창모드 강제 규칙: windowedOnly 해상도면 무조건 창모드
        if (_resList[data.resolutionIndex].windowedOnly)
            data.isFullScreen = false;

        // UI에 값 반영 (이벤트 미발화)
        resolutionDropdown.SetValueWithoutNotify(data.resolutionIndex);
        fullscreenToggle.SetIsOnWithoutNotify(data.isFullScreen);
        bgmSlider.SetValueWithoutNotify(data.bgmVolume);
        sfxSlider.SetValueWithoutNotify(data.sfxVolume);

        // 창모드 전용 해상도일 때 전체화면 토글 잠금
        RefreshFullscreenToggleInteractable(data.resolutionIndex);
    }

    private void ApplySettingsToSystem()
    {
        var data = OptionDataManager.Instance.Data;

        // 해상도/전체화면
        var r = _resList[data.resolutionIndex];
        bool fullscreen = r.windowedOnly ? false : data.isFullScreen;
        Screen.SetResolution(r.width, r.height, fullscreen);

        // 사운드 (AudioMixer 사용 시)
        ApplyMixerVolume(bgmParam, data.bgmVolume);
        ApplyMixerVolume(sfxParam, data.sfxVolume);
    }

    private void ApplyMixerVolume(string param, float linear01)
    {
        if (!audioMixer || string.IsNullOrEmpty(param)) return;
        // 0 -> -80dB, 1 -> 0dB
        float dB = (linear01 <= 0.0001f) ? -80f : Mathf.Log10(Mathf.Clamp(linear01, 0.0001f, 1f)) * 20f;
        audioMixer.SetFloat(param, dB);
    }

    private void RefreshFullscreenToggleInteractable(int resIndex)
    {
        bool windowedOnly = _resList[resIndex].windowedOnly;
        fullscreenToggle.interactable = !windowedOnly;
        if (windowedOnly && fullscreenToggle.isOn)
            fullscreenToggle.SetIsOnWithoutNotify(false);
    }

    // -------- UI 이벤트 --------

    private void OnResolutionDropdownChanged(int index)
    {
        var data = OptionDataManager.Instance.Data;
        data.resolutionIndex = index;

        // 창모드 전용 규칙 반영
        RefreshFullscreenToggleInteractable(index);
    }

    private void OnFullscreenToggleChanged(bool isFull)
    {
        var data = OptionDataManager.Instance.Data;

        // 만약 현재 선택 해상도가 창모드 전용이면 강제 false
        if (_resList[data.resolutionIndex].windowedOnly)
        {
            if (isFull) fullscreenToggle.SetIsOnWithoutNotify(false);
            data.isFullScreen = false;
        }
        else
        {
            data.isFullScreen = isFull;
        }
    }

    private void OnBGMSliderChanged(float v)
    {
        OptionDataManager.Instance.Data.bgmVolume = v;
        ApplyMixerVolume(bgmParam, v); // 실시간 미리듣기
        SoundManager.Instance?.ApplyVolumesFromOptions();
    }

    private void OnSFXSliderChanged(float v)
    {
        OptionDataManager.Instance.Data.sfxVolume = v;
        ApplyMixerVolume(sfxParam, v); // 실시간 미리듣기
        SoundManager.Instance?.ApplyVolumesFromOptions();
    }

    // -------- 버튼 핸들러 --------

    public void OnClickOpen()
    {
        // 열기 시 CanvasGroup 바로 1
        SetCanvasVisible(true, immediate: true);
        _isOpen = true;
    }

    public void OnClickClose()
    {
        // 닫기만: 저장 없이 닫기
        // 닫기 및 저장 시 기능 완료 후 CanvasGroup 0 == 이 함수는 저장 없음이므로 즉시 0
        SetCanvasVisible(false, immediate: true);
        _isOpen = false;
    }

    public void OnClickSave()
    {
        // 1) 현재 UI 값이 Data에 이미 반영되어 있음 (리스너)
        // 2) 시스템 적용
        ApplySettingsToSystem();
        // 3) 저장 및 사운드 적용
        OptionDataManager.Instance.Save();
        SoundManager.Instance?.ApplyVolumesFromOptions();
        // 4) 닫기
        SetCanvasVisible(false, immediate: true);
        _isOpen = false;
    }

    // -------- 표시 제어 --------

    private void SetCanvasVisible(bool visible, bool immediate)
    {
        if (!canvasGroup) return;

        float targetAlpha = visible ? 1f : 0f;
        canvasGroup.alpha = targetAlpha;
        canvasGroup.blocksRaycasts = visible;
        canvasGroup.interactable = visible;
    }

    // (선택) ESC 토글
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (_isOpen) OnClickClose();
            else OnClickOpen();
        }
    }
}
