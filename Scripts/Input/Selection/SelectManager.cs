using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>마우스 레이에서 선택 가능한 본체를 찾고 이전 선택의 해제를 보장합니다.</summary>
public class SelectManager : MonoBehaviour
{
    private int selectableLayerMask;
    // 현재 선택된 대상(인터페이스로 추적)
    private ITowerSelectable currentTowerSel;
    private ILabSelectable currentLabSel;
    private IEnemySelectable currentEnemySel;
    // 레이캐스트용 카메라
    private Camera raycastCamera;
    private void Awake()
    {
        if (!raycastCamera)
            raycastCamera = Camera.main;
        selectableLayerMask = ~LayerMask.GetMask("Ignore Raycast");
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
        if (!TryGetSelectableHit(ray, out var hit))
            return;
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
            pc.OnSelected();
            ActivateSelect(pc.transform);
            return;
        }

        // 4) Enemy 클릭 시
        var enemy = hit.collider.GetComponentInParent<Enemy>();
        if (enemy != null)
        {
            currentEnemySel = enemy;
            enemy.OnSelected();
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
                currentTowerSel = tc;
                tc.OnSelected();
                ActivateSelect(tb.transform);
            }
            else
            {
                currentTowerSel = tb;
                tb.OnSelected();
                ActivateSelect(tb.transform);
            }

            return;
        }

        // 5-1) TowerController 직접 클릭 시
        var towerCtrl = hit.collider.GetComponentInParent<TowerController>();
        if (towerCtrl != null)
        {
            currentTowerSel = towerCtrl;
            towerCtrl.OnSelected();
            var baseTf = towerCtrl.transform.parent;
            if (baseTf)
                ActivateSelect(baseTf);
            return;
        }

        // 6) 아무 것도 아니면: 이미 전부 OFF + 선택 해제 완료 상태
        if (currentTowerSel != null || currentLabSel != null || currentEnemySel != null)
        {
            ClearSelection();
        }
    }

    /// <summary>공격 범위 트리거와 이펙트를 제외하고 선택 가능한 본체만 레이 히트 후보로 사용합니다.</summary>
    private bool TryGetSelectableHit(Ray ray, out RaycastHit chosen)
    {
        var hits = Physics.RaycastAll(ray, Mathf.Infinity, selectableLayerMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
        {
            chosen = default;
            return false;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        // 충돌체 중에서 선택 가능한 오브젝트를 찾음
        foreach (var h in hits)
        {
            if (h.collider.isTrigger)
                continue;
            // 충돌체의 Transform을 가져옴
            var t = h.collider.transform;
            // 공격 대상 감지용 트리거는 타워 선택용 콜라이더가 아니다.
            if (t.GetComponentInParent<TowerRangeDetect>() != null)
                continue;
            // AttackRange/Select 비주얼 스킵
            var sel = t.GetComponentInParent<Select>();
            if (sel && (sel.VisualKind == Select.Kind.AttackRange || sel.VisualKind == Select.Kind.Select))
                continue;
            if (t.name == "AttackRange" || t.name == "Select")
                continue;
            // 실제 선택 가능한 타깃만 허용
            if (t.GetComponentInParent<TowerBase>() != null)
            {
                chosen = h;
                return true;
            }

            if (t.GetComponentInParent<TowerController>() != null)
            {
                chosen = h;
                return true;
            }

            if (t.GetComponentInParent<PlayerController>() != null)
            {
                chosen = h;
                return true;
            }

            if (t.GetComponentInParent<Enemy>() != null)
            {
                chosen = h;
                return true;
            }
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
    /// <param name = "root"></param>
    private void ActivateSelect(Transform root)
    {
        var visuals = root.GetComponentsInChildren<Select>(true);
        foreach (var v in visuals)
            if (v.VisualKind == Select.Kind.Select)
                v.gameObject.SetActive(true);
    }
}
