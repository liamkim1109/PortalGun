using UnityEngine;

public class PortalManager : MonoBehaviour
{
    public static PortalManager Instance { get; private set; }

    [SerializeField] private float surfaceClearance = 0.02f;
    [SerializeField] private float overlapPadding = 0.06f;
    [SerializeField] private int edgeSamples = 16;
    [SerializeField, Range(1, 5), Tooltip("Maximum nested portal views rendered per portal.")]
    private int maxPortalRecursion = 5;

    private Portal redPortal;
    private Portal bluePortal;
    private GameObject portalModelPrefab;
    private Vector2 portalFootprint = new Vector2(1.2f, 2f);
    private float portalDepth = 0.05f;
    private float surfaceOffset = 0.045f;
    private Quaternion modelAxisCorrection = Quaternion.identity;
    private Vector3 modelBoundsCenter;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        portalModelPrefab = Resources.Load<GameObject>("Portal_outline");

        if (portalModelPrefab == null)
        {
            Debug.LogWarning("Portal_outline.fbx was not found under an Assets/Resources folder.", this);
            return;
        }

        ConfigureModelAxesAndSize();
    }

    public void PlacePortal(
        PortalType type,
        Vector3 point,
        Vector3 surfaceNormal,
        Vector3 shotDirection,
        Vector3 playerUp,
        Collider hitSurface)
    {
        surfaceNormal.Normalize();
        if (Vector3.Dot(surfaceNormal, shotDirection) > 0f)
            surfaceNormal = -surfaceNormal;

        Quaternion rotation = MakePortalRotation(surfaceNormal, playerUp);
        Vector3 position = point + surfaceNormal * surfaceOffset;

        if (!FitsOnSurface(point, surfaceNormal, rotation, hitSurface))
        {
            Debug.Log("Portal placement rejected: the portal would extend past the hit surface.", this);
            return;
        }

        if (OverlapsOtherColor(type, position, rotation))
        {
            Debug.Log("Portal placement rejected: it would overlap the other portal.", this);
            return;
        }

        GameObject portalObject = new GameObject($"{type} Portal");
        portalObject.transform.SetPositionAndRotation(position, rotation);

        BoxCollider portalBounds = portalObject.AddComponent<BoxCollider>();
        portalBounds.size = new Vector3(portalFootprint.x, portalFootprint.y, portalDepth + overlapPadding * 2f);
        portalBounds.isTrigger = true;

        if (portalModelPrefab != null)
        {
            GameObject visual = Instantiate(portalModelPrefab, portalObject.transform);
            visual.name = "Portal Outline";
            visual.transform.localRotation = modelAxisCorrection;
            // FBX origins are not guaranteed to be at the mesh center. Centering the
            // visual prevents one side of the frame from being buried in the wall.
            visual.transform.localPosition = -(modelAxisCorrection * modelBoundsCenter);
            visual.transform.localScale = Vector3.one;
        }
        else
        {
            GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fallback.name = "Portal Outline (fallback)";
            fallback.transform.SetParent(portalObject.transform, false);
            Collider fallbackCollider = fallback.GetComponent<Collider>();
            if (fallbackCollider != null)
                Destroy(fallbackCollider);
        }

        Portal portal = portalObject.AddComponent<Portal>();
        portal.Initialize(type, GetPortalColor(type));
        portal.ConfigureView(portalFootprint, Camera.main, maxPortalRecursion);

        ReplacePortalReference(type, portal);
        LinkPortals();
    }

    private Quaternion MakePortalRotation(Vector3 normal, Vector3 preferredUp)
    {
        // Project the player's camera-up direction onto the portal plane to set its tilt.
        Vector3 up = Vector3.ProjectOnPlane(preferredUp, normal).normalized;
        if (up.sqrMagnitude < 0.001f)
            up = Vector3.ProjectOnPlane(Vector3.up, normal).normalized;
        if (up.sqrMagnitude < 0.001f)
            up = Vector3.ProjectOnPlane(Vector3.forward, normal).normalized;

        return Quaternion.LookRotation(normal, up);
    }

    private void ConfigureModelAxesAndSize()
    {
        Transform modelRoot = portalModelPrefab.transform;
        bool hasBounds = false;
        Bounds modelBounds = default;

        foreach (MeshFilter filter in portalModelPrefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
                continue;

            Bounds meshBounds = filter.sharedMesh.bounds;
            Matrix4x4 meshToRoot = modelRoot.worldToLocalMatrix * filter.transform.localToWorldMatrix;

            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 localCorner = meshBounds.center + Vector3.Scale(
                    meshBounds.extents,
                    new Vector3(
                        (corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f
                    )
                );
                Vector3 rootCorner = meshToRoot.MultiplyPoint3x4(localCorner);

                if (hasBounds)
                    modelBounds.Encapsulate(rootCorner);
                else
                {
                    modelBounds = new Bounds(rootCorner, Vector3.zero);
                    hasBounds = true;
                }
            }
        }

        if (!hasBounds)
        {
            Debug.LogWarning("Portal_outline.fbx contains no mesh; using default portal dimensions.", this);
            return;
        }

        Vector3 size = modelBounds.size;
        modelBoundsCenter = modelBounds.center;
        int normalAxis = IndexOfSmallest(size);
        int upAxis = IndexOfLargest(size);
        int rightAxis = 3 - normalAxis - upAxis;

        Vector3 sourceNormal = UnitAxis(normalAxis);
        Vector3 sourceUp = UnitAxis(upAxis);
        modelAxisCorrection = Quaternion.Inverse(Quaternion.LookRotation(sourceNormal, sourceUp));

        portalFootprint = new Vector2(size[rightAxis], size[upAxis]);
        portalDepth = size[normalAxis];
        surfaceOffset = portalDepth * 0.5f + surfaceClearance;
    }

    private int IndexOfSmallest(Vector3 value)
    {
        if (value.x <= value.y && value.x <= value.z)
            return 0;
        return value.y <= value.z ? 1 : 2;
    }

    private int IndexOfLargest(Vector3 value)
    {
        if (value.x >= value.y && value.x >= value.z)
            return 0;
        return value.y >= value.z ? 1 : 2;
    }

    private Vector3 UnitAxis(int index)
    {
        return index == 0 ? Vector3.right : index == 1 ? Vector3.up : Vector3.forward;
    }

    private Color GetPortalColor(PortalType type)
    {
        return type == PortalType.Red
            ? new Color(1f, 0.36f, 0.02f)
            : new Color(0.04f, 0.25f, 1f);
    }

    private bool FitsOnSurface(Vector3 center, Vector3 normal, Quaternion rotation, Collider surface)
    {
        if (surface == null)
            return false;

        Vector3 right = rotation * Vector3.right;
        Vector3 up = rotation * Vector3.up;
        Vector3 halfSize = portalFootprint * 0.5f;
        const float startOffset = 0.15f;
        const float rayLength = 0.3f;

        for (int i = 0; i < edgeSamples; i++)
        {
            float angle = i * (Mathf.PI * 2f / edgeSamples);
            Vector3 edgePoint = center
                + right * (Mathf.Cos(angle) * halfSize.x * 0.97f)
                + up * (Mathf.Sin(angle) * halfSize.y * 0.97f);
            Vector3 rayOrigin = edgePoint + normal * startOffset;

            if (!Physics.Raycast(rayOrigin, -normal, out RaycastHit hit, rayLength, ~0, QueryTriggerInteraction.Ignore) || hit.collider != surface || Vector3.Dot(hit.normal, normal) < 0.9f)
            {
                return false;
            }
        }

        return true;
    }

    private bool OverlapsOtherColor(PortalType type, Vector3 center, Quaternion rotation)
    {
        Vector3 halfExtents = new Vector3(
            portalFootprint.x * 0.5f + overlapPadding,
            portalFootprint.y * 0.5f + overlapPadding,
            portalDepth * 0.5f + overlapPadding
        );

        Collider[] overlaps = Physics.OverlapBox(
            center,
            halfExtents,
            rotation,
            ~0,
            QueryTriggerInteraction.Collide
        );

        foreach (Collider overlap in overlaps)
        {
            Portal otherPortal = overlap.GetComponent<Portal>();
            if (otherPortal != null && otherPortal.Type != type)
                return true;
        }

        return false;
    }

    private void ReplacePortalReference(PortalType type, Portal newPortal)
    {
        if (type == PortalType.Red)
        {
            if (redPortal != null)
                Destroy(redPortal.gameObject);

            redPortal = newPortal;
        }
        else
        {
            if (bluePortal != null)
                Destroy(bluePortal.gameObject);

            bluePortal = newPortal;
        }
    }

    private void LinkPortals()
    {
        if (redPortal != null)
            redPortal.SetLinkedPortal(bluePortal);

        if (bluePortal != null)
            bluePortal.SetLinkedPortal(redPortal);
    }
}
