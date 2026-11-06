using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public class PortalProjectile : MonoBehaviour
{
    private PortalManager portalManager;
    private PortalType portalType;
    private Transform owner;
    private Vector3 shotDirection;
    private Vector3 playerUp;
    private bool hasHit;
    private Material runtimeMaterial;

    public void Initialize(
        PortalManager manager,
        PortalType type,
        Transform projectileOwner,
        Vector3 direction,
        Vector3 cameraUp,
        float speed,
        Color color)
    {
        portalManager = manager;
        portalType = type;
        owner = projectileOwner;
        shotDirection = direction.normalized;
        playerUp = cameraUp;

        // Missed shots are cleaned up after five seconds; hits destroy the projectile sooner.
        Destroy(gameObject, 5f);

        Rigidbody body = GetComponent<Rigidbody>();
        body.useGravity = false;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearVelocity = direction.normalized * speed;

        Renderer projectileRenderer = GetComponent<Renderer>();
        if (projectileRenderer != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Standard");

            if (shader == null)
                Debug.LogError("No supported shader was found for the portal projectile.", this);
            else
            {
                runtimeMaterial = new Material(shader);

                if (runtimeMaterial.HasProperty("_BaseColor"))
                    runtimeMaterial.SetColor("_BaseColor", color);
                else if (runtimeMaterial.HasProperty("_Color"))
                    runtimeMaterial.SetColor("_Color", color);

                projectileRenderer.sharedMaterial = runtimeMaterial;
            }
        }

        Collider projectileCollider = GetComponent<Collider>();
        if (owner != null)
        {
            foreach (Collider ownerCollider in owner.GetComponentsInChildren<Collider>())
            {
                if (ownerCollider != null)
                    Physics.IgnoreCollision(projectileCollider, ownerCollider);
            }
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (hasHit)
            return;

        hasHit = true;

        if (portalManager != null && collision.contactCount > 0)
        {
            ContactPoint contact = collision.GetContact(0);
            portalManager.PlacePortal(
                portalType,
                contact.point,
                contact.normal,
                shotDirection,
                playerUp,
                collision.collider
            );
        }

        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
    }
}
