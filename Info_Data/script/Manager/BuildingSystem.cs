using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BuildingSystem : MonoBehaviour
{
    public static BuildingSystem Instance { get; private set; } // 싱글톤 인스턴스

    public enum GridOwner { None, Build, Move }

    private GridOwner currentOwner = GridOwner.None;
    private UnityEngine.Object ownerRef;                   // 누가 점유했는지 보관

    public GameObject cursorIndicatorParent;  // 움직일 커서 오브젝트
    public float gridSize = 1f;              // 그리드 간격
    public LayerMask groundLayer;            // 바닥을 위한 레이어
    public LayerMask obstacleLayer;          // 오브젝트 감지를 위한 레이어
    public LayerMask obstacleLayer2;          // 오브젝트 감지를 위한 레이어2

    public Color validColor = Color.green;
    public Color invalidColor = Color.red;

    [Header("네트워크 원격 배치를 위한 타워 베이스 프리팹")]
    public GameObject towerBasePrefab; // 타워 베이스 프리팹

    // 상태 확인용 프로퍼티
    public bool IsGridActive => cursorIndicatorParent && cursorIndicatorParent.activeSelf;
    public bool IsBuildableNow => isBuildable;

    private bool isBuildable = false; // 설치 가능 여부 저장
    private Renderer[] indicatorRenderers;
    private bool isCursorLocked = false; // 커서 잠금 상태

    private Dictionary<int, TowerBase> basesById = new();       // 타워 베이스 ID로 관리
    private int nextNetId = 1;                                  // 다음 타워 베이스 ID

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        if (cursorIndicatorParent != null)
            indicatorRenderers = cursorIndicatorParent.GetComponentsInChildren<Renderer>();
    }

    private void Update()
    {
        if (!isCursorLocked)
            HandleCursorMovement();
    }
    
    // 로컬 타워 베이스 등록
    public int RegisterBase(TowerBase tb)
    {
        int id = nextNetId++;
        tb.SetNetId(id);
        basesById[id] = tb;
        return id;
    }

    // 서버 타워 베이스 등록
    public void RegisterBase(TowerBase tb, int fixedId)
    {
        tb.SetNetId(fixedId);
        basesById[fixedId] = tb;            // 이미 있으면 갱신(또는 중복 체크)
    }

    // Network ID로 타워 베이스 검색
    public TowerBase GetBaseById(int id)
    {
        basesById.TryGetValue(id, out var tb);
        return tb;
    }

    // 타워 베이스 제거 및 레지스트리에서 삭제
    public void UnregisterBase(TowerBase tb)
    {
        if (tb == null) return;
        basesById.Remove(tb.NetId);
    }

    public bool IsBusy => currentOwner != GridOwner.None;
    public bool IsOwnedBy(GridOwner who) => currentOwner == who;

    // 예약 시작: 성공 시 true
    public bool BeginGrid(GridOwner owner, UnityEngine.Object who)
    {
        if (currentOwner != GridOwner.None && currentOwner != owner) return false;
        currentOwner = owner;
        ownerRef = who;
        SetGridActive(true);
        return true;
    }

    // 예약 종료: 같은 소유자만 해제 가능
    public void EndGrid(UnityEngine.Object who)
    {
        if (ownerRef != who) return;
        SetGridActive(false);
        currentOwner = GridOwner.None;
        ownerRef = null;
    }
    // 커서 위치 업데이트 및 장애물 체크
    private void HandleCursorMovement()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, Mathf.Infinity, groundLayer))
        {
            Vector3 alignedPosition = AlignToGrid(hit.point);
            alignedPosition.y = 0.1f;

            cursorIndicatorParent.transform.position = alignedPosition;

            // 장애물 존재 여부 판단
            bool hasObstacle = Physics.CheckBox(alignedPosition, Vector3.one * (gridSize / 2f), Quaternion.identity, obstacleLayer | obstacleLayer2);

            isBuildable = !hasObstacle; // ← 상태 저장
            UpdateIndicatorColor(isBuildable ? validColor : invalidColor);
        }
    }

    // 그리드에 맞춰 위치 정렬
    private Vector3 AlignToGrid(Vector3 pos)
    {
        float x = Mathf.Round(pos.x / gridSize) * gridSize;
        float y = Mathf.Round(pos.y / gridSize) * gridSize;
        float z = Mathf.Round(pos.z / gridSize) * gridSize;
        return new Vector3(x, y, z);
    }

    // 커서 색상 업데이트
    private void UpdateIndicatorColor(Color color)
    {
        if (indicatorRenderers == null) return;

        foreach (var renderer in indicatorRenderers)
        {
            if (renderer.material.HasProperty("_Color"))
            {
                renderer.material.color = color;
            }
        }
    }

    // 커서 잠금/해제
    public void LockCursor() => isCursorLocked = true;
    public void UnlockCursor() => isCursorLocked = false;

    // 네트워크에서 타워 베이스 배치 메시지를 처리
    public void HandleRemoteBasePlace(TowerBasePlaceMessage msg)
    {
        if (towerBasePrefab == null)
        {
            Debug.LogWarning("[BuildingSystem] towerBasePrefab 미지정");
            return;
        }

        // 1) 생성
        var obj = Instantiate(towerBasePrefab, msg.position, Quaternion.identity);

        // 2) 컴포넌트 취득
        if (!obj.TryGetComponent<TowerBase>(out var baseComp))
        {
            Debug.LogError("[BuildingSystem] TowerBase 컴포넌트가 없습니다.");
            return;
        }

        // 3) 소유자 찾아 주입
        PlayerController owner = null;
        var gm = GameManager.Instance;
        if (gm != null && msg.ownerIndex >= 0 && msg.ownerIndex < gm.players.Count)
            owner = gm.players[msg.ownerIndex];

        // 4) NetID 설정 + Initialize
        baseComp.SetNetId(msg.baseNetId);               // 필드명 baseNetId
        baseComp.Initialize(owner, msg.baseNetId);

        // 5) 레지스트리에 등록
        basesById[msg.baseNetId] = baseComp;            // 대입해야 등록됨
    }

    public void HandleTowerCreate(TowerCreateMessage msg)
    {
        var tb = GetBaseById(msg.baseNetId);
        if (tb != null)
            tb.CreateTowerRemote(msg.towerType, msg.level);
    }

    // 로컬 환경 건설 시도
    public GameObject TryBuild(GameObject buildPrefab)
    {
        if (!isBuildable)
        {
            Debug.Log("[BuildingSystem] 설치 불가 위치입니다.");
            return null;
        }

        Vector3 placePos = cursorIndicatorParent.transform.position;
        placePos.y = buildPrefab.transform.position.y;
        SoundManager.Instance.PlaySFX(SoundKey.Build);
        GameObject newObj = Instantiate(buildPrefab, placePos, Quaternion.identity);
        Debug.Log($"[BuildingSystem] 건설 완료: {placePos}");
        return newObj;
    }

    // 현재 커서(스냅) 좌표 가져오기 (그리드 정렬된 위치)
    public Vector3 CurrentAlignedCursorPosition()
    {
        return cursorIndicatorParent ? cursorIndicatorParent.transform.position : Vector3.zero;
    }

    // 그리드 표시 켜고/끄기
    public void SetGridActive(bool on)
    {
        if (!cursorIndicatorParent) return;     // 커서 오브젝트가 없으면 무시
        cursorIndicatorParent.SetActive(on);    // 그리드 표시 켜기/끄기
        if (on) UnlockCursor();                 // 그리드가 켜져있으면 커서 잠금 해제
        else LockCursor();                      // 그리드가 꺼져있으면 커서 잠금
    }

    // 원격 이동 반영 (서버 → 클라)
    public void HandleTowerMove(TowerMoveMessage msg)
    {
        var tb = GetBaseById(msg.baseNetId);
        if (!tb) return;

        // 베이스 이동
        tb.transform.position = msg.basePosition;

        // 타워가 있다면 같이 이동
        var tc = tb.GetComponentInChildren<TowerController>(true);
        if (msg.hasTower && tc)
            tc.transform.position = msg.towerPosition;
    }
}
