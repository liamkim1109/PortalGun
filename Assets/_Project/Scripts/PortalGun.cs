using UnityEngine;
using UnityEngine.InputSystem;

public class PortalGun : MonoBehaviour
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float projectileSpeed = 35f;
    [SerializeField] private float projectileRadius = 0.12f;

    private PortalManager portalManager;

    private void Awake()
    {
        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>();

        if (playerCamera == null)
            playerCamera = Camera.main;

        portalManager = PortalManager.Instance;
        if (portalManager == null)
        {
            GameObject managerObject = new GameObject("PortalManager");
            portalManager = managerObject.AddComponent<PortalManager>();
        }
    }

    // PlayerInput uses Send Messages, so these names match the RedPortal and BluePortal actions.
    public void OnRedPortal(InputValue value)
    {
        if (value.isPressed)
            Fire(PortalType.Red);
    }

    public void OnBluePortal(InputValue value)
    {
        if (value.isPressed)
            Fire(PortalType.Blue);
    }

    private void Fire(PortalType type)
    {
        if (playerCamera == null)
        {
            Debug.LogError("PortalGun needs a camera reference or a MainCamera-tagged camera.", this);
            return;
        }

        Color color = type == PortalType.Red
            ? new Color(1f, 0.36f, 0.02f)
            : new Color(0.04f, 0.25f, 1f);

        GameObject projectileObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        projectileObject.name = $"{type} Portal Projectile";
        projectileObject.transform.localScale = Vector3.one * (projectileRadius * 2f);
        projectileObject.transform.position = playerCamera.transform.position
            + playerCamera.transform.forward * (projectileRadius + 0.15f);

        projectileObject.AddComponent<Rigidbody>();
        PortalProjectile projectile = projectileObject.AddComponent<PortalProjectile>();
        projectile.Initialize(
            portalManager,
            type,
            transform,
            playerCamera.transform.forward,
            playerCamera.transform.up,
            projectileSpeed,
            color
        );
    }
}
