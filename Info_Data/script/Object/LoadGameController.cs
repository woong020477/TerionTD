using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LoadGameController : MonoBehaviour
{
    public static LoadGameController Instance { get; private set; }
    [SerializeField] private CanvasGroup cg;
    [SerializeField] private float fade = 0.12f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        if (!cg) cg = GetComponentInChildren<CanvasGroup>(true);
        cg.alpha = 0f; cg.blocksRaycasts = false; cg.interactable = false;
    }

    public void LoadNextScene(string target) => StartCoroutine(LoadRoutine(target));

    IEnumerator LoadRoutine(string target)
    {
        // 1) 페이드 인 (오버레이 표시)
        yield return Fade(1f);

        // 2) 다음 씬 로드 & 즉시 활성화
        var op = SceneManager.LoadSceneAsync(target, LoadSceneMode.Single);
        op.allowSceneActivation = true;
        while (!op.isDone) yield return null;

        // 3) 한 프레임 안정화 후 페이드 아웃
        yield return null;
        yield return Fade(0f);
    }

    IEnumerator Fade(float to)
    {
        cg.blocksRaycasts = (to > 0.5f);
        cg.interactable = (to > 0.5f);
        float t = 0f; float from = cg.alpha;
        while (t < fade)
        {
            t += Time.unscaledDeltaTime;
            cg.alpha = Mathf.Lerp(from, to, t / fade);
            yield return null;
        }
        cg.alpha = to;
        if (to < 0.5f) { cg.blocksRaycasts = false; cg.interactable = false; }
    }
}
