using UnityEngine;
using UnityEngine.EventSystems;

public class CameraManager : MonoBehaviour
{
    GameObject Player;
    
    float edgeSize = 2f;
    float moveSpeed = 50f;

    [Header("Camera Clamp Distance")]
    public float minX = 0f;
    public float maxX = 400f;
    public float minZ = -40f;
    public float maxZ = 400f;
    [HideInInspector] public Vector3 offset;

    [Header("Zoom Settings")]
    public float zoomSpeed = 20f;
    public float minZoom = 20f;
    public float maxZoom = 100f;
    public float zoomLerpSpeed = 10f;

    private float zoomVelocityY;    // SmoothDamp용 속도 추적 변수
    private float currentZoomY;     // 현재 줌 높이

    private Vector3 originalPosition;
    private bool isTempLocked = false;

    bool LockCamera = true;

    void Start()
    {
        if (Player == null)
        {
            Player = GameObject.FindWithTag("Player").gameObject;
        }
        offset = gameObject.transform.position; // 초기 카메라 위치를 보정 기준으로 설정
        gameObject.transform.rotation = Quaternion.Euler(80f, 0f, 0f);  // 초기 카메라 회전값 설정
        currentZoomY = offset.y;    // 초기 줌 높이를 높이 확인값으로 설정
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Y)) LockCamera = !LockCamera;      // 카메라 모드 전환
        if (!isTempLocked) HandleZoom();// 줌 기능 처리

        // T 키 누르기 시작 → 위치 저장 후 고정
        if (Input.GetKeyDown(KeyCode.T))
        {
            originalPosition = transform.position; // 현재 위치 저장
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
            return; // 전체 뷰 고정 중이면 나머지 동작 생략

        if (LockCamera)
            LockMode();
        else
            ScrollMode();


        if (Input.GetKey(KeyCode.Space))
        {
            float offsetZ = -currentZoomY * 0.125f;     // 카메라 줌에 따른 Z축 보정값
            gameObject.transform.position = new Vector3(Player.transform.position.x, transform.position.y, Player.transform.position.z + offsetZ);
        }

        ClampPos();
    }

    void HandleZoom()
    {
        if (EventSystem.current.IsPointerOverGameObject()) return; // UI 위에 마우스가 있을 때는 줌 기능 비활성화

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

        if (mousePos.x >= Screen.width - edgeSize) moveDir.x += 1f;
        else if (mousePos.x <= edgeSize) moveDir.x -= 1f;
        if (mousePos.y >= Screen.height - edgeSize) moveDir.z += 1f;
        else if (mousePos.y <= edgeSize) moveDir.z -= 1f;

        moveDir.Normalize();
        if (moveDir != Vector3.zero)
            gameObject.transform.Translate(moveDir * moveSpeed * Time.deltaTime, Space.World);
    }
    void LockMode()
    {
        float offsetZ = -currentZoomY * 0.125f;     // 카메라 줌에 따른 Z축 보정값
        Vector3 targetPos= new Vector3(Player.transform.position.x, transform.position.y, Player.transform.position.z + offsetZ);
        gameObject.transform.position = Vector3.Lerp(gameObject.transform.position, targetPos, Time.deltaTime * 10f);
    }
    void ClampPos()
    {
        Vector3 pos = transform.position;
        pos.x = Mathf.Clamp(pos.x, minX, maxX);
        pos.z = Mathf.Clamp(pos.z, minZ, maxZ);
        transform.position = pos;
    }
}
