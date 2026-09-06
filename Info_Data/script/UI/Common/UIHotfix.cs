using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>기존 화면의 UI 입력·표시 설정을 보정하는 호환 컴포넌트입니다.</summary>
public class UIHotfix : MonoBehaviour
{
    [Tooltip("풀스크린 배경/페이드가 덮을 때 자동으로 레이캐스트 끔")]
    public bool autoDisableFullscreenRaycast = true;
    void Start()
    {
        // 1) EventSystem 보정(없으면 생성)
        var allES = GetAll<EventSystem>();
        if (allES.Length == 0)
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
            Debug.LogWarning("EventSystem이 없어 생성했습니다.");
        }
        else if (allES.Length > 1)
        {
            Debug.LogWarning($"EventSystem이 {allES.Length}개 있습니다. 중복 제거 권장.");
        }

        // 2) Canvas 보정 - Camera/World는 worldCamera 지정
        var cam = Camera.main;
        foreach (var cv in GetAll<Canvas>())
        {
            if (cv.renderMode != RenderMode.ScreenSpaceOverlay && cv.worldCamera == null)
            {
                cv.worldCamera = cam;
                Debug.Log($"Canvas '{cv.name}' worldCamera 지정: {cam}");
            }

            // 3) GraphicRaycaster 보장
            if (cv.GetComponent<GraphicRaycaster>() == null)
            {
                cv.gameObject.AddComponent<GraphicRaycaster>();
                Debug.LogWarning($"Canvas '{cv.name}'에 GraphicRaycaster가 없어 추가했습니다.");
            }
        }

        // 4) 풀스크린 레이캐스트 차단자 자동 완화 (선택)
        if (autoDisableFullscreenRaycast)
        {
            var scr = Screen.safeArea;
            foreach (var img in GetAll<Image>())
            {
                if (!img.raycastTarget)
                    continue;
                var rt = img.rectTransform;
                var size = Vector2.Scale(rt.rect.size, rt.lossyScale);
                // 화면을 거의 덮는 배경류로 판단 (버튼/토글/드롭다운 제외)
                if (size.x >= scr.width * 0.95f && size.y >= scr.height * 0.95f)
                {
                    if (img.GetComponent<Button>() || img.GetComponent<Toggle>() || img.GetComponent<Dropdown>())
                        continue;
                    img.raycastTarget = false;
                    Debug.LogWarning($"풀스크린 Image '{img.name}'의 RaycastTarget을 껐습니다.");
                }
            }
        }
    }

    static T[] GetAll<T>()
        where T : Object
    {
#if UNITY_2023_1_OR_NEWER
        return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
#pragma warning disable CS0618
        return Object.FindObjectsOfType<T>(true);
#pragma warning restore CS0618
#endif
    }
}
