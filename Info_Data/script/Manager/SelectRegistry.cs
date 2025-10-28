using System.Collections.Generic;
using UnityEngine;

public static class SelectRegistry                                              // 선택 및 공격 범위 오브젝트 전역 레지스트리
{
    private static readonly HashSet<GameObject> _selects = new();               // 선택 오브젝트
    private static readonly HashSet<GameObject> _ranges = new();                // 공격 범위 오브젝트

    public static void Register(GameObject go, Select.Kind kind)                // 레지스트리에 등록
    {
        if (!go) return;                                                        // 오브젝트가 null인 경우 무시
        if (kind == Select.Kind.Select) _selects.Add(go);                       // 선택 오브젝트에 추가
        else _ranges.Add(go);                                                   // 공격 범위 오브젝트에 추가
        go.SetActive(false);                                                    // 기본적으로 비활성화
    }

    public static void Unregister(GameObject go, Select.Kind kind)              // 레지스트리에서 해제
    {
        if (!go) return;                                                        // 오브젝트가 null인 경우 무시
        if (kind == Select.Kind.Select) _selects.Remove(go);                    // 선택 오브젝트에서 제거
        else _ranges.Remove(go);                                                // 공격 범위 오브젝트에서 제거
    }

    public static void ClearAll()                                               // 모든 선택 및 범위 오브젝트 비활성화
    {
        foreach (var go in _selects) if (go) go.SetActive(false);               // 선택 오브젝트 비활성화
        foreach (var go in _ranges) if (go) go.SetActive(false);                // 공격 범위 오브젝트 비활성화
    }
}
