using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;


[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class PlayerController : MonoBehaviour
{
    
    [Header("References")]
    [SerializeField] private Transform playerCamera;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float moveAcceleration = 45f;
    [SerializeField] private float groundFriction = 8f;
    [SerializeField] private float jumpImpulse = 5f;
    [SerializeField, Min(1f)] private float maxLinearSpeed = 50f;

    [Header("Look")]
    [SerializeField] private float mouseSensitivity = 0.1f;
    [SerializeField] private float maxLookAngle = 85f;

    [Header("Body Visibility")]
    [SerializeField] private float bodyFadeStartAngle = 20f;
    [SerializeField] private float bodyFadeFullAngle = 55f;
    [SerializeField, Range(0f, 1f)] private float bodyFadeAlpha = 0.05f;

    private Rigidbody rb;
    private CapsuleCollider capsule;
    private PortalTraveller portalTraveller;
    private readonly Collider[] groundHits = new Collider[8];
    private Vector2 moveInput;
    private float cameraPitch;
    private bool jumpRequested;
    private readonly List<Material> bodyMaterials = new List<Material>();
    private readonly List<Color> originalBodyColors = new List<Color>();

    public Transform CameraTransform => playerCamera;
    public float MaxLinearSpeed => maxLinearSpeed;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        portalTraveller = GetComponent<PortalTraveller>();
        CacheBodyMaterials();

        // 마우스 회전으로 캡슐이 기울어지지 않도록 고정
        rb.constraints |= RigidbodyConstraints.FreezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.maxLinearVelocity = maxLinearSpeed;

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
        bool grounded = IsGrounded();
        MovePlayer(grounded);

        if (jumpRequested)
        {
            TryJump(grounded);
            jumpRequested = false;
        }

        ClampLinearSpeed();
        if (portalTraveller != null)
            portalTraveller.CaptureVelocityBeforePhysics(rb.linearVelocity);
    }

    private void LateUpdate()
    {
        // Looking down fades the visible capsule so it does not fill the camera view.
        float fade = Mathf.InverseLerp(bodyFadeStartAngle, bodyFadeFullAngle, cameraPitch);
        float alpha = Mathf.Lerp(1f, bodyFadeAlpha, fade);

        for (int i = 0; i < bodyMaterials.Count; i++)
        {
            Material material = bodyMaterials[i];
            Color color = originalBodyColors[i];
            color.a *= alpha;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
        }
    }

    private void CacheBodyMaterials()
    {
        foreach (Renderer bodyRenderer in GetComponentsInChildren<Renderer>(true))
        {
            foreach (Material material in bodyRenderer.materials)
            {
                if (material.HasProperty("_BaseColor"))
                    originalBodyColors.Add(material.GetColor("_BaseColor"));
                else if (material.HasProperty("_Color"))
                    originalBodyColors.Add(material.GetColor("_Color"));
                else
                    continue;

                MakeMaterialTransparent(material);
                bodyMaterials.Add(material);
            }
        }
    }

    private void MakeMaterialTransparent(Material material)
    {
        // Configure common URP and Built-in Standard shader transparency properties.
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_Mode"))
            material.SetFloat("_Mode", 3f);
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);

        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.DisableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    private void OnDestroy()
    {
        foreach (Material material in bodyMaterials)
        {
            if (material != null)
                Destroy(material);
        }
    }

    /// <summary>
    /// Keeps the camera's portal-mapped gaze direction while standing the body
    /// upright against world gravity. Returns the resulting body rotation.
    /// </summary>
    public Quaternion AlignAfterPortal(Quaternion mappedCameraRotation, Quaternion mappedBodyRotation)
    {
        Vector3 mappedForward = mappedCameraRotation * Vector3.forward;
        Vector3 flatForward = Vector3.ProjectOnPlane(mappedForward, Vector3.up);

        if (flatForward.sqrMagnitude < 0.001f)
            flatForward = Vector3.ProjectOnPlane(mappedBodyRotation * Vector3.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 0.001f)
            flatForward = Vector3.forward;

        Quaternion uprightBody = Quaternion.LookRotation(flatForward.normalized, Vector3.up);

        if (playerCamera != null)
        {
            float mappedPitch = -Mathf.Asin(Mathf.Clamp(Vector3.Dot(mappedForward.normalized, Vector3.up), -1f, 1f))
                * Mathf.Rad2Deg;
            cameraPitch = Mathf.Clamp(mappedPitch, -maxLookAngle, maxLookAngle);
            playerCamera.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
        }

        return uprightBody;
    }

    private void MovePlayer(bool grounded)
    {
        // 입력을 몸체의 방향에 맞춘 수평 이동 방향으로 변환
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;
        Vector3 moveDirection = forward * moveInput.y + right * moveInput.x;
        moveDirection = Vector3.ClampMagnitude(moveDirection, 1f);

        // 입력은 가속도로 반영한다. 공중에서 입력을 놓아도 포탈이 준 운동량은 유지된다.
        Vector3 velocity = rb.linearVelocity;
        Vector3 horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);

        if (moveDirection.sqrMagnitude > 0.001f)
        {
            // Steering adds acceleration in the input direction. It does not pull
            // a fast portal exit velocity back down to the normal walk speed.
            float speedAlongInput = Vector3.Dot(horizontalVelocity, moveDirection);
            if (speedAlongInput < moveSpeed)
            {
                float acceleration = Mathf.Min(
                    moveAcceleration * Time.fixedDeltaTime,
                    moveSpeed - speedAlongInput
                );
                horizontalVelocity += moveDirection * acceleration;
            }
        }
        else if (grounded)
        {
            horizontalVelocity = Vector3.MoveTowards(
                horizontalVelocity,
                Vector3.zero,
                groundFriction * Time.fixedDeltaTime
            );
        }

        rb.linearVelocity = horizontalVelocity + Vector3.up * velocity.y;
    }

    private void ClampLinearSpeed()
    {
        Vector3 velocity = rb.linearVelocity;
        if (velocity.sqrMagnitude > maxLinearSpeed * maxLinearSpeed)
            rb.linearVelocity = velocity.normalized * maxLinearSpeed;
    }

    private void TryJump(bool grounded)
    {
        if (!grounded)
            return;

        // 기존의 아래 방향 속도를 지운 뒤 점프 힘 적용
        Vector3 velocity = rb.linearVelocity;
        velocity.y = 0f;
        rb.linearVelocity = velocity;

        rb.AddForce(Vector3.up * jumpImpulse, ForceMode.Impulse);
    }

    private bool IsGrounded()
    {
        Bounds bounds = capsule.bounds;

        // 캡슐 중심이 아니라 실제 캡슐 밑면 근처에서 검사한다.
        Vector3 checkPosition = new Vector3(bounds.center.x, bounds.min.y + 0.08f, bounds.center.z);
        const float checkRadius = 0.2f;

        // OverlapSphere는 플레이어 캡슐과도 겹치므로 자기 콜라이더는 제외한다.
        int overlapCount = Physics.OverlapSphereNonAlloc(
            checkPosition,
            checkRadius,
            groundHits,
            ~0,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < overlapCount; i++)
        {
            if (groundHits[i] != capsule)
                return true;
        }

        return false;
    }
}
