using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>게임 카메라의 이동·줌과 화면 제한을 처리합니다.</summary>
public class CameraManager : MonoBehaviour
{
    private PlayerController localPlayer;
    float edgeSize = 2f;
    float moveSpeed = 50f;
    [Header("Camera Clamp Distance")]
    public float minX = 0f;
    public float maxX = 400f;
    public float minZ = -40f;
    public float maxZ = 400f;
    [HideInInspector]
    public Vector3 offset;
    [Header("Zoom Settings")]
    public float zoomSpeed = 20f;
    public float minZoom = 20f;
    public float maxZoom = 100f;
    public float zoomLerpSpeed = 10f;
    private float zoomVelocityY; // SmoothDamp용 속도 추적 변수
    private float currentZoomY; // 현재 줌 높이
    private Vector3 originalPosition;
    private bool isTempLocked = false;
    bool LockCamera = true;
    void Start()
    {
        offset = gameObject.transform.position;
        gameObject.transform.rotation = Quaternion.Euler(80f, 0f, 0f);
        currentZoomY = offset.y;
        TryBindLocalPlayer();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Y))
            LockCamera = !LockCamera;
        if (!isTempLocked)
            HandleZoom();
        // T 키 누르기 시작 → 위치 저장 후 고정
        if (Input.GetKeyDown(KeyCode.T))
        {
            originalPosition = transform.position;
            isTempLocked = true;
            transform.position = new Vector3(100f, 150f, 65f);
        }

        // T 키 뗐을 때 → 원래 위치 복원
        if (Input.GetKeyUp(KeyCode.T))
        {
            isTempLocked = false;
            transform.position = originalPosition;
        }
    }

    void LateUpdate()
    {
        if (isTempLocked)
            return;
        // GameManager와 플레이어의 Start 순서에 의존하지 않고 로컬 대상이 준비되면 바인딩합니다.
        if (localPlayer == null && !TryBindLocalPlayer())
            return;
        if (LockCamera)
            LockMode();
        else
            ScrollMode();
        if (Input.GetKey(KeyCode.Space))
        {
            float offsetZ = -currentZoomY * 0.125f;
            gameObject.transform.position = new Vector3(localPlayer.transform.position.x, transform.position.y, localPlayer.transform.position.z + offsetZ);
        }

        ClampPos();
    }

    void HandleZoom()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            // 입력 감지 → 목표 줌 높이 갱신 (scroll값이 작기 때문에 10f 만큼 증폭함)
            currentZoomY = Mathf.Clamp(currentZoomY - scroll * zoomSpeed * 10f, minZoom, maxZoom);
        }

        // 현재 카메라 y 위치 보간 (추적 여부와 상관없이 일정하게)
        float newY = Mathf.SmoothDamp(gameObject.transform.position.y, currentZoomY, ref zoomVelocityY, 0.15f);
        Vector3 camPos = gameObject.transform.position;
        camPos.y = newY;
        gameObject.transform.position = camPos;
    }

    void ScrollMode()
    {
        Vector3 moveDir = Vector3.zero;
        float h = Input.GetAxisRaw("ArrowHorizontal");
        float v = Input.GetAxisRaw("ArrowVertical");
        moveDir = new Vector3(h, 0f, v);
        Vector3 mousePos = Input.mousePosition;
        if (mousePos.x >= Screen.width - edgeSize)
            moveDir.x += 1f;
        else if (mousePos.x <= edgeSize)
            moveDir.x -= 1f;
        if (mousePos.y >= Screen.height - edgeSize)
            moveDir.z += 1f;
        else if (mousePos.y <= edgeSize)
            moveDir.z -= 1f;
        moveDir.Normalize();
        if (moveDir != Vector3.zero)
            gameObject.transform.Translate(moveDir * moveSpeed * Time.deltaTime, Space.World);
    }

    void LockMode()
    {
        float offsetZ = -currentZoomY * 0.125f;
        Vector3 targetPos = new Vector3(localPlayer.transform.position.x, transform.position.y, localPlayer.transform.position.z + offsetZ);
        gameObject.transform.position = Vector3.Lerp(gameObject.transform.position, targetPos, Time.deltaTime * 10f);
    }

    /// <summary>
    /// 태그로 첫 번째 플레이어를 찾지 않고, GameManager가 판정한 로컬 플레이어를 추적 대상으로 사용합니다.
    /// </summary>
    private bool TryBindLocalPlayer()
    {
        GameManager gameManager = GameManager.Instance;
        if (gameManager == null)
            return false;
        localPlayer = gameManager.LocalPlayer;
        return localPlayer != null;
    }

    void ClampPos()
    {
        Vector3 pos = transform.position;
        pos.x = Mathf.Clamp(pos.x, minX, maxX);
        pos.z = Mathf.Clamp(pos.z, minZ, maxZ);
        transform.position = pos;
    }
}
