using System.IO;
using UnityEngine;

[System.Serializable]
public class OptionData
{
    public float bgmVolume = 0.5f;     // 0.0 ~ 1.0 (기본 50%)
    public float sfxVolume = 0.5f;     // 0.0 ~ 1.0 (기본 50%)
    public int resolutionIndex = 1;    // 0:1280x720, 1:1366x768, 2:1920x1080, 3:2560x1440 (기본 1366x768)
    public bool isFullScreen = false;  // 기본 창모드
}

public class OptionDataManager : MonoBehaviour
{
    public static OptionDataManager Instance { get; private set; }

    private string FilePath => Path.Combine(Application.persistentDataPath, "Option.json");

    public OptionData Data { get; private set; } = new OptionData();

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
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
            if (Data == null) Data = new OptionData(); // 파손 대비
        }
        else
        {
            Data = new OptionData(); // 기본값
            Save();                  // 파일 생성
        }
#if UNITY_EDITOR
        Debug.Log($"[OptionDataManager] Loaded: {FilePath}\n{JsonUtility.ToJson(Data, true)}");
#endif
    }

    public void Save()
    {
        string json = JsonUtility.ToJson(Data, prettyPrint: true);
        File.WriteAllText(FilePath, json);
#if UNITY_EDITOR
        Debug.Log($"[OptionDataManager] Saved: {FilePath}\n{json}");
#endif
    }
}
