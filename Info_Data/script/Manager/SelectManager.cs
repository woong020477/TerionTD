using UnityEngine;
using UnityEngine.EventSystems;

public class SelectManager : MonoBehaviour
{
    // 현재 선택된 대상(인터페이스로 추적)
    private ITowerSelectable currentTowerSel;
    private ILabSelectable currentLabSel;
    private IEnemySelectable currentEnemySel;

    // 레이캐스트용 카메라
    private Camera raycastCamera;

    private void Awake()
    {
        if (!raycastCamera) raycastCamera = Camera.main; // 1회 캐시
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject())
            HandleClick();
    }

    private void HandleClick()
    {
        // 0) 이전 선택 해제(UI 닫기)
        ClearSelection();

        // 1) 레지에 저장된 선택 및 공격범위 전부 OFF
        SelectRegistry.ClearAll();

        // 2) 레이캐스트
        var ray = raycastCamera.ScreenPointToRay(Input.mousePosition);
        if (!TryGetSelectableHit(ray, out var hit))return;                  // 레이캐스트로 선택 가능한 오브젝트가 있는지 확인

        // 2-1) 클릭 대상이 제외 레이어에 속하는지 확인
        Transform tr = hit.collider.transform;

        // AttackRange와 Select는 선택 대상에서 제외
        var sel = tr.GetComponentInParent<Select>();
        if (sel != null && (sel.VisualKind == Select.Kind.AttackRange || sel.VisualKind == Select.Kind.Select))
            return;

        // 3) PlayerController 클릭 시
        var pc = hit.collider.GetComponentInParent<PlayerController>();
        if (pc != null)
        {
            currentLabSel = pc;
            pc.OnSelected();        // UI 열기 (연구소)
            ActivateSelect(pc.transform);
            return;
        }

        // 4) Enemy 클릭 시
        var enemy = hit.collider.GetComponentInParent<Enemy>();
        if (enemy != null)
        {
            currentEnemySel = enemy;
            enemy.OnSelected();     // UI 열기 (적 정보)
            ActivateSelect(enemy.transform);
            return;
        }

        // 5) 분기: TowerBase 클릭 시
        var tb = hit.collider.GetComponentInParent<TowerBase>();
        if (tb != null)
        {
            // 베이스에 타워가 있으면 타워 UI/범위 + 베이스 Select
            var tc = tb.GetComponentInChildren<TowerController>(true);
            if (tc != null)
            {
                tc.OnSelected();
                ActivateRange(tc.transform);          // AttackRange ON
                ActivateSelect(tb.transform);         // 베이스 Select ON
            }
            else
            {
                tb.OnSelected();                      // 베이스 UI
                ActivateSelect(tb.transform);
            }
            return;
        }

        // 5-1) TowerController 직접 클릭 시
        var towerCtrl = hit.collider.GetComponentInParent<TowerController>();
        if (towerCtrl != null)
        {
            towerCtrl.OnSelected();
            ActivateRange(towerCtrl.transform);       // AttackRange ON
            var baseTf = towerCtrl.transform.parent;  // 베이스 Select ON
            if (baseTf) ActivateSelect(baseTf);
            return;
        }
        
        // 6) 아무 것도 아니면: 이미 전부 OFF + 선택 해제 완료 상태
        if(currentTowerSel != null || currentLabSel != null || currentEnemySel != null)
        {
            ClearSelection(); // 선택 해제
        }
    }

    // 유효한 선택 가능한 오브젝트가 있는지 레이캐스트로 확인합니다.
    private bool TryGetSelectableHit(Ray ray, out RaycastHit chosen)
    {
        var hits = Physics.RaycastAll(ray, Mathf.Infinity, ~0, QueryTriggerInteraction.Collide);                            // 레이캐스트로 모든 충돌체를 가져옴
        if (hits == null || hits.Length == 0) { chosen = default; return false; }                                           // 레이캐스트 결과가 없으면 false

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));                                                // 거리순 정렬

        // 충돌체 중에서 선택 가능한 오브젝트를 찾음
        foreach (var h in hits)
        {
            // 충돌체의 Transform을 가져옴
            var t = h.collider.transform;

            // AttackRange/Select 비주얼 스킵
            var sel = t.GetComponentInParent<Select>();
            if (sel && (sel.VisualKind == Select.Kind.AttackRange || sel.VisualKind == Select.Kind.Select))
                continue;
            if (t.name == "AttackRange" || t.name == "Select") // 안전망
                continue;

            // 실제 선택 가능한 타깃만 허용
            if (t.GetComponent<TowerBase>() != null)        { chosen = h; return true; }
            if (t.GetComponent<TowerController>() != null)  { chosen = h; return true; }
            if (t.GetComponent<PlayerController>() != null) { chosen = h; return true; }
            if (t.GetComponentInParent<Enemy>() != null)    { chosen = h; return true; }
        }

        chosen = default;
        return false;
    }

    private void ClearSelection()
    {
        // 이전 선택 대상들에게 UI 닫기 기회 제공
        currentTowerSel?.OnDeSelected();
        currentLabSel?.OnDeSelected();
        currentEnemySel?.OnDeSelected();

        currentTowerSel = null;
        currentLabSel = null;
        currentEnemySel = null;
    }

    /// <summary>
    /// 타워베이스, 플레이어, 적 클릭 시 해당 오브젝트의 Select를 활성화합니다.
    /// </summary>
    /// <param name="root"></param>
    private void ActivateSelect(Transform root)                                                     // 타워베이스 / 플레이어베이스 / 적 클릭 시
    {
        var visuals = root.GetComponentsInChildren<Select>(true);                                   // true: 비활성화된 것도 포함
        foreach (var v in visuals)                                                                  // Select 컴포넌트가 있는 모든 자식 오브젝트
            if (v.VisualKind == Select.Kind.Select)                                                 // Select 종류인 경우
                v.gameObject.SetActive(true);                                                       // 해당 오브젝트 활성화
    }

    /// <summary>
    /// 타워 클릭 시 해당 타워의 AttackRange를 활성화합니다.
    /// </summary>
    /// <param name="root"></param>
    private void ActivateRange(Transform root)                                                      // 타워 클릭 시
    {
        var visuals = root.GetComponentsInChildren<Select>(true);                                   // true: 비활성화된 것도 포함
        foreach (var v in visuals)                                                                  // Select 컴포넌트가 있는 모든 자식 오브젝트
            if (v.VisualKind == Select.Kind.AttackRange)                                            // AttackRange 종류인 경우
                v.gameObject.SetActive(true);                                                       // 해당 오브젝트 활성화
    }
}
