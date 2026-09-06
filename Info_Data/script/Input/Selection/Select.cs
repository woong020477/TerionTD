using UnityEngine;

/// <summary>선택 표시 오브젝트를 레지스트리에 등록하고 수명주기 종료 시 해제합니다.</summary>
public class Select : MonoBehaviour
{
    public enum Kind
    {
        Select,
        AttackRange
    }

    [SerializeField]
    private Kind kind = Kind.Select; // 선택 종류 설정
    public Kind VisualKind => kind;

    private void Awake()
    {
        gameObject.SetActive(false);
        SelectRegistry.Register(gameObject, kind);
    }

    private void OnDestroy()
    {
        SelectRegistry.Unregister(gameObject, kind);
    }
}
