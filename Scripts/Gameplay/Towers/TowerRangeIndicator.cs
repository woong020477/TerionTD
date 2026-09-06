using UnityEngine;
using UnityEngine.Rendering;

/// <summary>SphereCollider의 월드 반경을 LineRenderer로 표현하며 클릭용 콜라이더를 추가하지 않습니다.</summary>
[DisallowMultipleComponent]
public sealed class TowerRangeIndicator : MonoBehaviour
{
    private const int SegmentCount = 96;
    private static readonly Color RangeColor = new Color(0.1f, 1f, 0.2f, 0.95f);
    private SphereCollider rangeCollider;
    private LineRenderer rangeLine;
    private Material runtimeMaterial;
    public void Initialize(SphereCollider sourceCollider)
    {
        rangeCollider = sourceCollider;
        EnsureLineRenderer();
        Hide();
    }

    public void Show()
    {
        if (rangeCollider == null)
            return;
        EnsureLineRenderer();
        RebuildCircle();
        rangeLine.enabled = true;
    }

    public void Hide()
    {
        if (rangeLine != null)
            rangeLine.enabled = false;
    }

    private void LateUpdate()
    {
        if (rangeLine != null && rangeLine.enabled)
            RebuildCircle();
    }

    private void EnsureLineRenderer()
    {
        if (rangeLine != null)
            return;
        GameObject lineObject = new GameObject("RangeOutline");
        lineObject.transform.SetParent(transform, false);
        lineObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        rangeLine = lineObject.AddComponent<LineRenderer>();
        rangeLine.useWorldSpace = true;
        rangeLine.loop = true;
        rangeLine.positionCount = SegmentCount;
        rangeLine.startWidth = 0.12f;
        rangeLine.endWidth = 0.12f;
        rangeLine.startColor = RangeColor;
        rangeLine.endColor = RangeColor;
        rangeLine.numCornerVertices = 2;
        rangeLine.numCapVertices = 2;
        rangeLine.shadowCastingMode = ShadowCastingMode.Off;
        rangeLine.receiveShadows = false;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            runtimeMaterial = new Material(shader);
            runtimeMaterial.color = RangeColor;
            if (runtimeMaterial.HasProperty("_BaseColor"))
                runtimeMaterial.SetColor("_BaseColor", RangeColor);
            rangeLine.sharedMaterial = runtimeMaterial;
        }
    }

    private void RebuildCircle()
    {
        Transform colliderTransform = rangeCollider.transform;
        Vector3 center = colliderTransform.TransformPoint(rangeCollider.center);
        center.y += 0.08f;
        Vector3 scale = colliderTransform.lossyScale;
        float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        float worldRadius = rangeCollider.radius * maxScale;
        for (int i = 0; i < SegmentCount; i++)
        {
            float angle = i * Mathf.PI * 2f / SegmentCount;
            rangeLine.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * worldRadius, 0f, Mathf.Sin(angle) * worldRadius));
        }
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
    }
}
