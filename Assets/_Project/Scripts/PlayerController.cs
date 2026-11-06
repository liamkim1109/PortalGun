using UnityEngine;
using UnityEngine.InputSystem;


public class PlayerController : MonoBehaviour
{
    
    [Header("References")]
    [SerializeField] private Transform playerCamera;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float jumpImpulse = 5f;

    [Header("Look")]
    [SerializeField] private float mouseSensitivity = 0.1f;
    [SerializeField] private float maxLookAngle = 85f;

    private Rigidbody rb;
    private Vector2 moveInput;
    private float cameraPitch;
    private bool jumpRequested;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        // 마우스 회전으로 캡슐이 기울어지지 않도록 고정
        rb.constraints |= RigidbodyConstraints.FreezeRotation;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // Input Action "Move" (Value / Vector2)
    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    // Input Action "Look" (Value / Vector2), 바인딩: <Mouse>/delta
    public void OnLook(InputValue value)
    {
        Vector2 lookInput = value.Get<Vector2>();

        // 몸체는 좌우로 회전
        float yaw = lookInput.x * mouseSensitivity;
        rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, yaw, 0f));

        // 카메라는 위아래로만 회전
        cameraPitch -= lookInput.y * mouseSensitivity;
        cameraPitch = Mathf.Clamp(cameraPitch, -maxLookAngle, maxLookAngle);

        if (playerCamera != null)
            playerCamera.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
    }

    // Input Action "Jump" (Button)
    public void OnJump(InputValue value)
    {
        if (value.isPressed)
            jumpRequested = true;
    }

    private void FixedUpdate()
    {
        MovePlayer();

        if (jumpRequested)
        {
            TryJump();
            jumpRequested = false;
        }
    }

    private void MovePlayer()
    {
        // 입력을 몸체의 방향에 맞춘 수평 이동 방향으로 변환
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;
        Vector3 moveDirection = forward * moveInput.y + right * moveInput.x;
        moveDirection = Vector3.ClampMagnitude(moveDirection, 1f);

        // Y축 속도는 유지해 중력과 점프에 의한 이동을 보존
        Vector3 velocity = rb.linearVelocity;
        Vector3 targetHorizontalVelocity = moveDirection * moveSpeed;
        rb.linearVelocity = new Vector3(targetHorizontalVelocity.x, velocity.y, targetHorizontalVelocity.z);
    }

    private void TryJump()
    {
        if (!IsGrounded())
            return;

        // 기존의 아래 방향 속도를 지운 뒤 점프 힘 적용
        Vector3 velocity = rb.linearVelocity;
        velocity.y = 0f;
        rb.linearVelocity = velocity;

        rb.AddForce(Vector3.up * jumpImpulse, ForceMode.Impulse);
    }

    private bool IsGrounded()
    {
        CapsuleCollider capsule = GetComponent<CapsuleCollider>();
        Bounds bounds = capsule.bounds;

        // 캡슐 중심이 아니라 실제 캡슐 밑면 근처에서 검사한다.
        Vector3 checkPosition = new Vector3(bounds.center.x, bounds.min.y + 0.08f, bounds.center.z);
        const float checkRadius = 0.2f;

        // OverlapSphere는 플레이어 캡슐과도 겹치므로 자기 콜라이더는 제외한다.
        Collider[] overlaps = Physics.OverlapSphere(checkPosition, checkRadius, ~0, QueryTriggerInteraction.Ignore);

        foreach (Collider other in overlaps)
        {
            if (other != capsule)
                return true;
        }

        return false;
    }
}
