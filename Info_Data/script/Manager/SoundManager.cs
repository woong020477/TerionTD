using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public enum SoundKey
{
    /* 사운드 키 Enum */
    BGM,
    UI_Click,
    Build,
    Fire_Laser,
    Fire_Flame,
    Fire_Rocket,
    Fire_Boom,
    Fire_Bullet
}

[System.Serializable]
public struct SoundEntry
{
    public SoundKey key;             // Enum 키
    public AudioClip clip;           // 사운드 파일
    public bool isBgm;               // 배경음이면 true (loop)
    public bool loop;                // BGM 외 루프가 필요한 경우
    [Range(0f, 1f)] public float volume; // 개별 기본 볼륨 (옵션 볼륨과 곱해짐)

    public float VolumeOrDefault => (volume <= 0f ? 1f : volume);
}

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("Mixer")]
    private AudioMixer mixer;
    [SerializeField] private AudioMixerGroup bgmGroup;
    [SerializeField] private AudioMixerGroup sfxGroup;
    [SerializeField] private string bgmParam = "BGM";      // OptionManager와 동일 노출 파라미터명
    [SerializeField] private string sfxParam = "SFX";

    [Header("클립 매핑")]
    [SerializeField] private List<SoundEntry> clips = new();

    [SerializeField] private AudioSource bgmSource;        // BGM 전용(루프/크로스페이드)
    [SerializeField] private AudioSource sfxSource;        // OneShot 전용(겹쳐 재생 가능)

    [Header("기본 BGM")]
    [SerializeField] private SoundKey startupBgm = SoundKey.BGM;
    [SerializeField] private bool playBgmOnInit = true;

    // 내부
    readonly Dictionary<SoundKey, SoundEntry> dict = new();
    Coroutine bgmFadeCo;
    bool audioEnabled = true;          // SoundOn/Off 상태

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        mixer = OptionManager.Instance.audioMixer;

        // 맵 빌드
        dict.Clear();
        for (int i = 0; i < clips.Count; i++)
        {
            var e = clips[i];
            if (!e.clip) continue;
            dict[e.key] = e;
        }

        // 소스 준비
        if (!bgmSource) bgmSource = gameObject.AddComponent<AudioSource>();
        if (!sfxSource) sfxSource = gameObject.AddComponent<AudioSource>();
        bgmSource.playOnAwake = false; bgmSource.loop = true;  // BGM 루프 기본
        sfxSource.playOnAwake = false; sfxSource.loop = false;

        if (bgmGroup) bgmSource.outputAudioMixerGroup = bgmGroup;
        if (sfxGroup) sfxSource.outputAudioMixerGroup = sfxGroup;

        ApplyVolumesFromOptions(); // 믹서가 없을 때도 옵션 값 반영
    }

    void Start()
    {
        if (playBgmOnInit && dict.ContainsKey(startupBgm))
            PlayBGM(startupBgm, fade: 0.3f);
    }

    // --------- 외부 API ---------

    /// <summary>초기화/재초기화. BGM 스타트/볼륨 재적용 등</summary>
    public void Init(bool playBgm = true)
    {
        ApplyVolumesFromOptions();
        if (playBgm && dict.ContainsKey(startupBgm))
            PlayBGM(startupBgm, fade: 0.3f);
    }

    /// <summary>사운드 전역 On</summary>
    public void SoundOn()
    {
        audioEnabled = true;
        bgmSource.mute = false;
        sfxSource.mute = false;

        // 믹서를 쓰면 OptionManager가 볼륨을 올려주므로 여기선 무음 해제만
        if (!mixer) ApplyVolumesFromOptions();
    }

    /// <summary>사운드 전역 Off(음소거)</summary>
    public void SoundOff()
    {
        audioEnabled = false;
        bgmSource.mute = true;
        sfxSource.mute = true;
        // 믹서를 쓰면 파라미터를 직접 -80dB로 내릴 수도 있지만
        // OptionManager와 충돌 피하려고 소스 mute만 사용
    }

    /// <summary>BGM 재생(클립 교체+크로스페이드)</summary>
    public void PlayBGM(SoundKey key, float fade = 0.25f)
    {
        if (!dict.TryGetValue(key, out var e) || !e.clip) return;
        if (!audioEnabled) return;

        bgmSource.loop = e.isBgm || e.loop;
        if (bgmFadeCo != null) StopCoroutine(bgmFadeCo);
        bgmFadeCo = StartCoroutine(Co_CrossFadeBgm(e.clip, e.VolumeOrDefault, fade));
    }

    public void StopBGM(float fade = 0.25f)
    {
        if (bgmFadeCo != null) StopCoroutine(bgmFadeCo);
        if (fade <= 0f) { bgmSource.Stop(); return; }
        StartCoroutine(Co_FadeOut(bgmSource, fade));
    }

    /// <summary>SFX 재생(OneShot)</summary>
    public void PlaySFX(SoundKey key, float pitch = 1f)
    {
        if (!dict.TryGetValue(key, out var e) || !e.clip) return;
        if (!audioEnabled) return;

        sfxSource.pitch = pitch;
        float baseVol = e.VolumeOrDefault;
        float sfxVol = GetSfxScalar();                 // 옵션/믹서 반영 값
        sfxSource.PlayOneShot(e.clip, baseVol * sfxVol);
    }

    /// <summary>옵션 값(OptionDataManager)에서 볼륨을 적용. 믹서가 없을 때 사용됨</summary>
    public void ApplyVolumesFromOptions()
    {
        var data = OptionDataManager.Instance?.Data;
        float bgm = data != null ? Mathf.Clamp01(data.bgmVolume) : 0.5f;
        float sfx = data != null ? Mathf.Clamp01(data.sfxVolume) : 0.5f;

        if (mixer)
        {
            // Mixer 사용: OptionManager가 이미 같은 파라미터를 만지고 있으므로
            // 여기서도 안전하게 맞춰줌(있어도/없어도 무방)
            ApplyMixerVolume(bgmParam, bgm);
            ApplyMixerVolume(sfxParam, sfx);
        }
        else
        {
            // Mixer 미사용: AudioSource.volume로 직접 반영
            bgmSource.volume = bgm;
            sfxSource.volume = sfx;
        }
    }

    /// <summary>BGM/SFX 개별 볼륨 세팅(OptionManager에서 이벤트로 호출할 때 사용)</summary>
    public void SetBgmVolume(float linear01) => ApplyMixerVolume(bgmParam, linear01);
    public void SetSfxVolume(float linear01) => ApplyMixerVolume(sfxParam, linear01);

    // --------- 내부 유틸 ---------

    IEnumerator Co_CrossFadeBgm(AudioClip next, float entryVol, float t)
    {
        if (!bgmSource.isPlaying)
        {
            bgmSource.clip = next;
            bgmSource.volume = 0f;
            bgmSource.Play();
            yield return Co_FadeTo(bgmSource, entryVol * GetBgmScalar(), t);
            yield break;
        }

        // 페이드 아웃
        yield return Co_FadeTo(bgmSource, 0f, t);
        bgmSource.Stop();
        bgmSource.clip = next;
        bgmSource.Play();
        // 페이드 인
        yield return Co_FadeTo(bgmSource, entryVol * GetBgmScalar(), t);
    }

    IEnumerator Co_FadeTo(AudioSource src, float target, float t)
    {
        if (t <= 0f) { src.volume = target; yield break; }
        float start = src.volume;
        float time = 0f;
        while (time < t)
        {
            time += Time.unscaledDeltaTime;
            src.volume = Mathf.Lerp(start, target, time / t);
            yield return null;
        }
        src.volume = target;
    }

    IEnumerator Co_FadeOut(AudioSource src, float t)
    {
        float start = src.volume;
        float time = 0f;
        while (time < t)
        {
            time += Time.unscaledDeltaTime;
            src.volume = Mathf.Lerp(start, 0f, time / t);
            yield return null;
        }
        src.Stop();
        src.volume = start;
    }

    void ApplyMixerVolume(string param, float linear01)
    {
        if (!mixer || string.IsNullOrEmpty(param)) return;
        float dB = (linear01 <= 0.0001f) ? -80f : Mathf.Log10(Mathf.Clamp(linear01, 0.0001f, 1f)) * 20f;
        mixer.SetFloat(param, dB);
    }

    // OptionData or Mixer 반영값 스칼라(OneShot 볼륨계산용)
    float GetBgmScalar()
    {
        if (!mixer || string.IsNullOrEmpty(bgmParam)) return OptionDataManager.Instance?.Data.bgmVolume ?? 1f;
        if (mixer.GetFloat(bgmParam, out var dB)) return Mathf.Pow(10f, dB / 20f);
        return 1f;
    }
    float GetSfxScalar()
    {
        if (!mixer || string.IsNullOrEmpty(sfxParam)) return OptionDataManager.Instance?.Data.sfxVolume ?? 1f;
        if (mixer.GetFloat(sfxParam, out var dB)) return Mathf.Pow(10f, dB / 20f);
        return 1f;
    }
}
