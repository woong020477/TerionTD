using UnityEngine;

public class Select : MonoBehaviour
{
    public enum Kind { Select, AttackRange }                // 선택 종류
    [SerializeField] private Kind kind = Kind.Select;       // 선택 종류 설정
    public Kind VisualKind => kind;                         // 선택 종류 반환

    private void Awake()
    {
        gameObject.SetActive(false);                        // 기본 OFF
        SelectRegistry.Register(gameObject, kind);          // 레지스트리에 등록
    }

    private void OnDestroy()
    {
        SelectRegistry.Unregister(gameObject, kind);        // 레지스트리에서 해제
    }
}