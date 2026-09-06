using System.Collections.Generic;
using UnityEngine;

/// <summary>선택 표시와 범위 표시 오브젝트를 등록하여 선택 해제 시 함께 숨깁니다.</summary>
public static class SelectRegistry
{
    private static readonly HashSet<GameObject> _selects = new(); // 선택 오브젝트
    private static readonly HashSet<GameObject> _ranges = new(); // 공격 범위 오브젝트
    public static void Register(GameObject go, Select.Kind kind)
    {
        if (!go)
            return;
        if (kind == Select.Kind.Select)
            _selects.Add(go);
        else
            _ranges.Add(go);
        go.SetActive(false);
    }

    public static void Unregister(GameObject go, Select.Kind kind)
    {
        if (!go)
            return;
        if (kind == Select.Kind.Select)
            _selects.Remove(go);
        else
            _ranges.Remove(go);
    }

    public static void ClearAll()
    {
        foreach (var go in _selects)
            if (go)
                go.SetActive(false);
        foreach (var go in _ranges)
            if (go)
                go.SetActive(false);
    }
}
