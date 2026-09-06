using System.IO;
using UnityEngine;

[System.Serializable]
public class OptionData
{
    public float bgmVolume = 0.5f; // 0.0 ~ 1.0 (기본 50%)
    public float sfxVolume = 0.5f; // 0.0 ~ 1.0 (기본 50%)
    public int resolutionIndex = 1; // 0:1280x720, 1:1366x768, 2:1920x1080, 3:2560x1440 (기본 1366x768)
    public bool isFullScreen = false; // 기본 창모드
}

/// <summary>옵션 값을 저장하고 다음 실행에서 사용할 설정을 복원합니다.</summary>
public class OptionDataManager : MonoBehaviour
{
    public static OptionDataManager Instance { get; private set; }
    private string FilePath => Path.Combine(Application.persistentDataPath, "Option.json");
    public OptionData Data { get; private set; } = new OptionData();

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadOrCreate();
    }

    public void LoadOrCreate()
    {
        if (File.Exists(FilePath))
        {
            string json = File.ReadAllText(FilePath);
            Data = JsonUtility.FromJson<OptionData>(json);
            if (Data == null)
                Data = new OptionData();
        }
        else
        {
            Data = new OptionData();
            Save();
        }
#if UNITY_EDITOR
        Debug.Log($"[OptionDataManager] Loaded: {FilePath}");
#endif
    }

    public void Save()
    {
        string json = JsonUtility.ToJson(Data, prettyPrint: true);
        File.WriteAllText(FilePath, json);
#if UNITY_EDITOR
        Debug.Log($"[OptionDataManager] Saved: {FilePath}");
#endif
    }
}
