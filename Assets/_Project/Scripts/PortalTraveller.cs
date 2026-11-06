using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class PortalTraveller : MonoBehaviour
{
    [SerializeField] private float exitClearance = 0.08f;
    [SerializeField] private float frameMargin = 0.12f;
    [SerializeField] private float teleportCooldown = 0.2f;
    [SerializeField, Range(0.5f, 1f)] private float requiredOpeningFraction = 0.67f;

    private Rigidbody body;
    private CapsuleCollider capsule;
    private float lastTeleportTime = float.NegativeInfinity;
    private Portal lastExitPortal;
    private Vector3 velocityBeforePhysics;
    private bool hasVelocitySnapshot;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
    }

    // Called by PlayerController immediately before Unity simulates the next step.
    // A portal trigger can arrive after a wall/floor contact has already reduced
    // the live Rigidbody velocity, so keep the incoming value for the transfer.
    public void CaptureVelocityBeforePhysics(Vector3 velocity)
    {
        velocityBeforePhysics = velocity;
        hasVelocitySnapshot = true;
    }

    public void TryTraverse(Portal entry)
    {
        if (entry == null || entry.LinkedPortal == null)
            return;

        if (Time.time - lastTeleportTime < teleportCooldown)
            return;

        Portal exit = entry.LinkedPortal;

        if (entry == lastExitPortal)
        {
            if (StillOverlappingPortal(entry))
                return;
            lastExitPortal = null;
        }

        Vector3 capsuleCenter = capsule.transform.TransformPoint(capsule.center);
        float signedDistance = Vector3.Dot(capsuleCenter - entry.transform.position, entry.transform.forward);

        Vector3 incomingVelocity = body.linearVelocity;
        if (hasVelocitySnapshot)
        {
            float side = Mathf.Sign(signedDistance);
            float currentIntoSpeed = -Vector3.Dot(incomingVelocity, entry.transform.forward) * side;
            float savedIntoSpeed = -Vector3.Dot(velocityBeforePhysics, entry.transform.forward) * side;
            if (savedIntoSpeed > currentIntoSpeed)
                incomingVelocity = velocityBeforePhysics;
        }

        // Permit traversal from either side. Velocity or gravity must point toward
        // the portal plane from the side the capsule is currently on.
        float towardSpeed = Vector3.Dot(incomingVelocity, entry.transform.forward) * Mathf.Sign(signedDistance);
        float gravityToward = Vector3.Dot(Physics.gravity, entry.transform.forward) * Mathf.Sign(signedDistance);
        bool movingIntoPortal = towardSpeed < -0.05f;
        bool gravityPullsIntoPortal = gravityToward < -0.05f;

        if (!movingIntoPortal && !gravityPullsIntoPortal)
            return;

        if (!FitsThroughOpening(entry))
            return;

        // A front-side entry needs the usual half turn. A back-side entry keeps
        // its orientation so its incoming velocity still points out of the exit.
        Quaternion halfTurn = signedDistance >= 0f
            ? Quaternion.AngleAxis(180f, Vector3.up)
            : Quaternion.identity;
        Quaternion rotationMap = exit.transform.rotation
            * halfTurn
            * Quaternion.Inverse(entry.transform.rotation);

        Vector3 localPosition = entry.transform.InverseTransformPoint(body.position);
        localPosition = halfTurn * localPosition;

        Vector3 worldScale = transform.lossyScale;
        float radius = capsule.radius * Mathf.Max(Mathf.Abs(worldScale.x), Mathf.Abs(worldScale.z));
        float halfHeight = Mathf.Max(capsule.height * Mathf.Abs(worldScale.y) * 0.5f, radius);
        float cylinderHalfHeight = halfHeight - radius;
        float exitNormalExtent = radius + cylinderHalfHeight
            * Mathf.Abs(Vector3.Dot(Vector3.up, exit.transform.forward));

        // Put the whole capsule just outside the destination surface.
        localPosition.z = exitNormalExtent + exitClearance;
        Vector3 exitPosition = exit.transform.TransformPoint(localPosition);

        // Keep only the velocity that was actually moving into the entry plane.
        // Tangential motion is intentionally discarded so side speed cannot turn
        // into an upward launch when the exit portal has a different orientation.
        float inwardSpeed = Mathf.Max(
            0f,
            -Vector3.Dot(incomingVelocity, entry.transform.forward) * Mathf.Sign(signedDistance)
        );
        Vector3 mappedVelocity = exit.transform.forward * inwardSpeed;
        PlayerController controller = GetComponent<PlayerController>();
        float speedLimit = controller != null ? controller.MaxLinearSpeed : body.maxLinearVelocity;
        if (mappedVelocity.sqrMagnitude > speedLimit * speedLimit)
            mappedVelocity = mappedVelocity.normalized * speedLimit;

        Vector3 mappedAngularVelocity = rotationMap * body.angularVelocity;
        Quaternion mappedRotation = rotationMap * body.rotation;
        Quaternion uprightRotation = controller != null && controller.CameraTransform != null
            ? controller.AlignAfterPortal(rotationMap * controller.CameraTransform.rotation, mappedRotation)
            : GetUprightRotation(mappedRotation, exit.transform);

        body.position = exitPosition;
        body.rotation = uprightRotation;
        body.linearVelocity = mappedVelocity;
        body.angularVelocity = mappedAngularVelocity;
        lastTeleportTime = Time.time;
        lastExitPortal = exit;
    }

    private bool StillOverlappingPortal(Portal portal)
    {
        BoxCollider portalBounds = portal.GetComponent<BoxCollider>();
        if (portalBounds == null)
            return false;

        Vector3 worldScale = transform.lossyScale;
        float radius = capsule.radius * Mathf.Max(Mathf.Abs(worldScale.x), Mathf.Abs(worldScale.z));
        float halfHeight = Mathf.Max(capsule.height * Mathf.Abs(worldScale.y) * 0.5f, radius);
        float cylinderHalfHeight = halfHeight - radius;
        Vector3 capsuleAxis = capsule.transform.up;
        float extentRight = radius + cylinderHalfHeight
            * Mathf.Abs(Vector3.Dot(capsuleAxis, portal.transform.right));
        float extentUp = radius + cylinderHalfHeight
            * Mathf.Abs(Vector3.Dot(capsuleAxis, portal.transform.up));
        float extentNormal = radius + cylinderHalfHeight
            * Mathf.Abs(Vector3.Dot(capsuleAxis, portal.transform.forward));
        Vector3 localCenter = portal.transform.InverseTransformPoint(
            capsule.transform.TransformPoint(capsule.center)
        );

        return Mathf.Abs(localCenter.x) <= portalBounds.size.x * 0.5f + extentRight
            && Mathf.Abs(localCenter.y) <= portalBounds.size.y * 0.5f + extentUp
            && Mathf.Abs(localCenter.z) <= portalBounds.size.z * 0.5f + extentNormal;
    }

    private bool FitsThroughOpening(Portal portal)
    {
        BoxCollider portalBounds = portal.GetComponent<BoxCollider>();
        if (portalBounds == null)
            return false;

        Vector3 worldScale = transform.lossyScale;
        float radius = capsule.radius * Mathf.Max(Mathf.Abs(worldScale.x), Mathf.Abs(worldScale.z));
        float halfHeight = Mathf.Max(capsule.height * Mathf.Abs(worldScale.y) * 0.5f, radius);
        float cylinderHalfHeight = halfHeight - radius;
        Vector3 capsuleAxis = capsule.transform.up;

        float extentRight = radius + cylinderHalfHeight
            * Mathf.Abs(Vector3.Dot(capsuleAxis, portal.transform.right));
        float extentUp = radius + cylinderHalfHeight
            * Mathf.Abs(Vector3.Dot(capsuleAxis, portal.transform.up));

        Vector3 capsuleCenter = capsule.transform.TransformPoint(capsule.center);
        Vector3 localCenter = portal.transform.InverseTransformPoint(capsuleCenter);
        // Allow the teleport once about two thirds of the capsule's projected
        // width and height fit inside the opening, instead of demanding a perfect fit.
        float outsideFraction = 1f - requiredOpeningFraction;
        float allowedHalfWidth = portalBounds.size.x * 0.5f - extentRight * outsideFraction - frameMargin;
        float allowedHalfHeight = portalBounds.size.y * 0.5f - extentUp * outsideFraction - frameMargin;

        return allowedHalfWidth > 0f
            && allowedHalfHeight > 0f
            && Mathf.Abs(localCenter.x) <= allowedHalfWidth
            && Mathf.Abs(localCenter.y) <= allowedHalfHeight;
    }

    private Quaternion GetUprightRotation(Quaternion mappedRotation, Transform exit)
    {
        // The body teleports as a whole, so restore an upright view relative to world gravity.
        Vector3 forward = Vector3.ProjectOnPlane(mappedRotation * Vector3.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
        {
            Vector3 right = Vector3.ProjectOnPlane(mappedRotation * Vector3.right, Vector3.up);
            if (right.sqrMagnitude > 0.001f)
                forward = Vector3.Cross(right.normalized, Vector3.up);
        }

        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.ProjectOnPlane(exit.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;

        return Quaternion.LookRotation(forward.normalized, Vector3.up);
    }
}
