using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>건설·이동의 그리드 점유와 플레이어 영역 검사를 한 경로에서 처리합니다.</summary>
public class BuildingSystem : MonoBehaviour
{
    public static BuildingSystem Instance { get; private set; }

    public enum GridOwner
    {
        None,
        Build,
        Move
    }

    private GridOwner currentOwner = GridOwner.None;
    private UnityEngine.Object ownerRef; // 누가 점유했는지 보관
    private PlayerController gridPlayer; // 현재 그리드를 사용할 수 있는 로컬 플레이어
    public GameObject cursorIndicatorParent; // 움직일 커서 오브젝트
    public float gridSize = 1f; // 그리드 간격
    public LayerMask groundLayer; // 바닥을 위한 레이어
    public LayerMask obstacleLayer; // 오브젝트 감지를 위한 레이어
    public LayerMask obstacleLayer2; // 오브젝트 감지를 위한 레이어2
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
    private readonly Dictionary<NetworkEntityKey, TowerBase> basesById = new();
    private int nextNetId = 1; // 이 클라이언트 소유 베이스의 다음 로컬 ID
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
    public int RegisterBase(TowerBase tb, int ownerIndex)
    {
        int id = nextNetId++;
        tb.SetNetId(id);
        basesById[new NetworkEntityKey(ownerIndex, id)] = tb;
        return id;
    }

    // 서버 타워 베이스 등록
    public void RegisterBase(TowerBase tb, int ownerIndex, int fixedId)
    {
        tb.SetNetId(fixedId);
        basesById[new NetworkEntityKey(ownerIndex, fixedId)] = tb;
    }

    // 플레이어별 로컬 ID를 조합해 타워 베이스 검색
    public TowerBase GetBaseById(int ownerIndex, int id)
    {
        basesById.TryGetValue(new NetworkEntityKey(ownerIndex, id), out var tb);
        return tb;
    }

    // 타워 베이스 제거 및 레지스트리에서 삭제
    public void UnregisterBase(TowerBase tb)
    {
        if (tb == null)
            return;
        int ownerIndex = tb.Owner != null ? tb.Owner.PlayerIndex : -1;
        var key = new NetworkEntityKey(ownerIndex, tb.NetId);
        if (key.IsValid && basesById.TryGetValue(key, out var registered) && registered == tb)
            basesById.Remove(key);
    }

    public bool IsBusy => currentOwner != GridOwner.None;

    public bool IsOwnedBy(GridOwner who) => currentOwner == who;
    // 예약 시작: 성공 시 true
    public bool BeginGrid(GridOwner owner, UnityEngine.Object who, PlayerController playerOwner)
    {
        if (playerOwner == null || !playerOwner.IsLocalPlayer)
            return false;
        if (currentOwner != GridOwner.None && currentOwner != owner)
            return false;
        currentOwner = owner;
        ownerRef = who;
        gridPlayer = playerOwner;
        SetGridActive(true);
        return true;
    }

    // 예약 종료: 같은 소유자만 해제 가능
    public void EndGrid(UnityEngine.Object who)
    {
        if (ownerRef != who)
            return;
        SetGridActive(false);
        currentOwner = GridOwner.None;
        ownerRef = null;
        gridPlayer = null;
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
            isBuildable = !hasObstacle && IsInsidePlayerTerritory(alignedPosition, gridPlayer);
            UpdateIndicatorColor(isBuildable ? validColor : invalidColor);
        }
        else
        {
            isBuildable = false;
            UpdateIndicatorColor(invalidColor);
        }
    }

    /// <summary>배치 지점이 요청 플레이어 영역에 속하는지 검사해 다른 라인 건설을 막습니다.</summary>
    private static bool IsInsidePlayerTerritory(Vector3 position, PlayerController requester)
    {
        if (requester == null || GameManager.Instance == null)
            return false;
        float requesterDistance = HorizontalSqrDistance(position, requester.transform.position);
        foreach (var player in GameManager.Instance.players)
        {
            if (player == null || player == requester)
                continue;
            if (HorizontalSqrDistance(position, player.transform.position) < requesterDistance)
                return false;
        }

        return true;
    }

    private static float HorizontalSqrDistance(Vector3 a, Vector3 b)
    {
        float x = a.x - b.x;
        float z = a.z - b.z;
        return x * x + z * z;
    }

    public bool CanPlaceFor(PlayerController requester)
    {
        return requester != null && requester == gridPlayer && requester.IsLocalPlayer && isBuildable;
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
        if (indicatorRenderers == null)
            return;
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

        // UDP 중복 수신 시 같은 베이스를 다시 만들지 않는다.
        if (GetBaseById(msg.ownerIndex, msg.baseNetId) != null)
            return;
        // 1) 생성
        var obj = Instantiate(towerBasePrefab, msg.position, Quaternion.identity);
        // 2) 컴포넌트 취득
        if (!obj.TryGetComponent<TowerBase>(out var baseComp))
        {
            Debug.LogError("[BuildingSystem] TowerBase 컴포넌트가 없습니다.");
            Destroy(obj);
            return;
        }

        // 3) 소유자 찾아 주입
        PlayerController owner = null;
        var gm = GameManager.Instance;
        if (gm != null && msg.ownerIndex >= 0 && msg.ownerIndex < gm.players.Count)
            owner = gm.players[msg.ownerIndex];
        if (owner == null)
        {
            Debug.LogWarning($"[BuildingSystem] 플레이어 {msg.ownerIndex}를 찾지 못해 베이스 생성을 취소합니다.");
            Destroy(obj);
            return;
        }

        // 4) NetID 설정 + Initialize
        baseComp.Initialize(owner, msg.baseNetId);
        // 5) 레지스트리에 등록
        RegisterBase(baseComp, msg.ownerIndex, msg.baseNetId);
    }

    public void HandleTowerCreate(TowerCreateMessage msg)
    {
        var tb = GetBaseById(msg.ownerIndex, msg.baseNetId);
        if (tb != null)
            tb.CreateTowerRemote(msg.towerType, msg.level);
    }

    /// <summary>요청자 소유권과 현재 건설 가능 상태를 검사한 뒤 배치를 확정합니다.</summary>
    public GameObject TryBuild(PlayerController requester, GameObject buildPrefab)
    {
        if (!CanPlaceFor(requester))
        {
            Debug.Log("[BuildingSystem] 설치 불가 위치입니다.");
            return null;
        }

        Vector3 placePos = cursorIndicatorParent.transform.position;
        placePos.y = buildPrefab.transform.position.y;
        SoundManager.Instance.PlaySFX(SoundKey.Build);
        GameObject newObj = Instantiate(buildPrefab, placePos, Quaternion.identity);
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
        if (!cursorIndicatorParent)
            return;
        cursorIndicatorParent.SetActive(on);
        if (on)
            UnlockCursor();
        else
            LockCursor();
    }

    // 원격 이동 반영 (서버 → 클라)
    public void HandleTowerMove(TowerMoveMessage msg)
    {
        var tb = GetBaseById(msg.ownerIndex, msg.baseNetId);
        if (!tb)
            return;
        // 베이스 이동
        tb.transform.position = msg.basePosition;
        // 타워가 있다면 같이 이동
        var tc = tb.GetComponentInChildren<TowerController>(true);
        if (msg.hasTower && tc)
            tc.transform.position = msg.towerPosition;
    }
}
