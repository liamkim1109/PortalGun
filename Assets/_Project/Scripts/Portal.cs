using UnityEngine;

public class Portal : MonoBehaviour
{
    private const int PortalViewLayer = 31;
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

    public PortalType Type { get; private set; }
    public Portal LinkedPortal { get; private set; }
    private Material runtimeMaterial;
    private Material viewMaterial;
    private RenderTexture viewTexture;
    private Camera viewCamera;
    private Camera sourceCamera;
    private GameObject viewSurface;
    private Renderer viewSurfaceRenderer;
    private Renderer[] destinationRenderers = new Renderer[0];
    private bool[][] destinationRendererStatesByLevel = new bool[0][];
    private Camera[] recursionCameras = new Camera[0];
    private RenderTexture[] recursionTextures = new RenderTexture[0];
    private readonly Plane[] cameraFrustumPlanes = new Plane[6];
    private BoxCollider portalBounds;
    private int recursionLimit = 1;

    public BoxCollider BoundsCollider => portalBounds;

    private void Awake()
    {
        portalBounds = GetComponent<BoxCollider>();
    }

    public void Initialize(PortalType type, Color color)
    {
        Type = type;
        name = $"{type} Portal";

        Renderer[] portalRenderers = GetComponentsInChildren<Renderer>(true);
        if (portalRenderers.Length > 0)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Standard");

            if (shader == null)
            {
                Debug.LogError("No supported shader was found for the portal visual.", this);
                return;
            }

            runtimeMaterial = new Material(shader);

            if (runtimeMaterial.HasProperty("_BaseColor"))
                runtimeMaterial.SetColor("_BaseColor", color);
            else if (runtimeMaterial.HasProperty("_Color"))
                runtimeMaterial.SetColor("_Color", color);

            foreach (Renderer portalRenderer in portalRenderers)
            {
                portalRenderer.sharedMaterial = runtimeMaterial;
                portalRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                portalRenderer.receiveShadows = false;
            }
        }
    }

    public void SetLinkedPortal(Portal other)
    {
        if (LinkedPortal != other)
        {
            LinkedPortal = other;
            destinationRenderers = other != null
                ? other.GetComponentsInChildren<Renderer>(true)
                : new Renderer[0];
            destinationRendererStatesByLevel = new bool[recursionLimit][];
            for (int i = 0; i < recursionLimit; i++)
                destinationRendererStatesByLevel[i] = new bool[destinationRenderers.Length];
        }
        if (viewCamera != null)
            viewCamera.enabled = false;
        if (viewSurface != null)
            viewSurface.SetActive(other != null);
    }

    public void ConfigureView(Vector2 footprint, Camera playerCamera, int maxRecursionDepth)
    {
        sourceCamera = playerCamera;
        recursionLimit = Mathf.Clamp(maxRecursionDepth, 1, 5);

        float apertureAspect = footprint.x * 0.9f / (footprint.y * 0.94f);
        const int textureWidth = 640;
        int textureHeight = Mathf.Max(256, Mathf.RoundToInt(textureWidth / apertureAspect));
        float textureAspect = textureWidth / (float)textureHeight;
        viewTexture = new RenderTexture(textureWidth, textureHeight, 24, RenderTextureFormat.ARGB32)
        {
            name = $"{name} View",
            useMipMap = false,
            autoGenerateMips = false
        };
        viewTexture.Create();

        viewSurface = GameObject.CreatePrimitive(PrimitiveType.Quad);
        viewSurface.name = "Portal View Surface";
        viewSurface.layer = PortalViewLayer;
        viewSurface.transform.SetParent(transform, false);
        viewSurface.transform.localPosition = Vector3.forward * 0.015f;
        viewSurface.transform.localRotation = Quaternion.identity;
        viewSurface.transform.localScale = new Vector3(footprint.x * 0.9f, footprint.y * 0.94f, 1f);

        Collider surfaceCollider = viewSurface.GetComponent<Collider>();
        if (surfaceCollider != null)
            Destroy(surfaceCollider);

        Shader shader = Shader.Find("Portal/PortalViewMask");

        if (shader != null)
        {
            viewMaterial = new Material(shader);
            viewMaterial.SetTexture(BaseMapId, viewTexture);
            if (viewMaterial.HasProperty("_BaseColor"))
                viewMaterial.SetColor("_BaseColor", Color.white);
            if (viewMaterial.HasProperty("_ApertureAspect"))
                viewMaterial.SetFloat("_ApertureAspect", textureAspect);
            if (viewMaterial.HasProperty("_TextureAspect"))
                viewMaterial.SetFloat("_TextureAspect", textureAspect);

            viewSurfaceRenderer = viewSurface.GetComponent<Renderer>();
            viewSurfaceRenderer.sharedMaterial = viewMaterial;
            viewSurfaceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            viewSurfaceRenderer.receiveShadows = false;
        }

        GameObject cameraObject = new GameObject("Portal View Camera");
        cameraObject.transform.SetParent(transform, false);
        viewCamera = cameraObject.AddComponent<Camera>();
        viewCamera.enabled = false;
        viewCamera.targetTexture = viewTexture;
        viewCamera.allowHDR = false;
        viewCamera.allowMSAA = false;
        viewCamera.cullingMask = sourceCamera != null
            ? sourceCamera.cullingMask & ~(1 << PortalViewLayer)
            : ~(1 << PortalViewLayer);

        recursionCameras = new Camera[recursionLimit];
        recursionTextures = new RenderTexture[recursionLimit];
        recursionCameras[0] = viewCamera;
        recursionTextures[0] = viewTexture;

        for (int i = 1; i < recursionLimit; i++)
        {
            // Deeper views occupy a smaller portion of the final image. Lower
            // their resolution at each step to reduce memory and render cost.
            int divisor = 1 << Mathf.Min(i, 4);
            RenderTexture texture = new RenderTexture(
                Mathf.Max(128, viewTexture.width / divisor),
                Mathf.Max(128, viewTexture.height / divisor),
                24,
                RenderTextureFormat.ARGB32
            )
            {
                name = $"{name} Recursion {i + 1}",
                useMipMap = false,
                autoGenerateMips = false
            };
            texture.Create();
            recursionTextures[i] = texture;

            GameObject nestedCameraObject = new GameObject($"Portal View Camera {i + 1}");
            nestedCameraObject.transform.SetParent(transform, false);
            Camera nestedCamera = nestedCameraObject.AddComponent<Camera>();
            nestedCamera.enabled = false;
            nestedCamera.targetTexture = texture;
            nestedCamera.allowHDR = false;
            nestedCamera.allowMSAA = false;
            recursionCameras[i] = nestedCamera;
        }

        SetLinkedPortal(LinkedPortal);
    }

    private void LateUpdate()
    {
        if (LinkedPortal == null || viewCamera == null)
            return;

        if (sourceCamera == null)
            sourceCamera = Camera.main;
        if (sourceCamera == null)
            return;

        // Portal openings are one-sided: never show the remote view from behind
        // the surface, where the wall should occlude it.
        bool cameraOnFrontSide = IsOnFrontSide(sourceCamera);
        if (viewSurfaceRenderer != null)
            viewSurfaceRenderer.enabled = LinkedPortal != null && cameraOnFrontSide;
        if (!cameraOnFrontSide || !IsInsideCameraFrustum(sourceCamera))
            return;

        RenderRecursiveView(sourceCamera, 0, recursionLimit);
    }

    private RenderTexture RenderRecursiveView(Camera source, int level, int levelsRemaining)
    {
        if (levelsRemaining <= 0 || level >= recursionCameras.Length || LinkedPortal == null)
            return null;

        Camera camera = recursionCameras[level];
        if (!ConfigurePortalCamera(camera, source))
            return null;

        Texture previousTexture = viewMaterial != null
            ? viewMaterial.GetTexture(BaseMapId)
            : null;
        bool[] rendererStates = destinationRendererStatesByLevel[level];
        bool ownSurfaceWasEnabled = viewSurfaceRenderer != null && viewSurfaceRenderer.enabled;
        bool destinationSurfaceWasEnabled = LinkedPortal.viewSurfaceRenderer != null
            && LinkedPortal.viewSurfaceRenderer.enabled;

        try
        {
            if (levelsRemaining > 1 && IsOnFrontSide(camera) && IsInsideCameraFrustum(camera))
            {
                RenderTexture nestedTexture = RenderRecursiveView(camera, level + 1, levelsRemaining - 1);
                if (viewMaterial != null && nestedTexture != null)
                    viewMaterial.SetTexture(BaseMapId, nestedTexture);
                if (viewSurfaceRenderer != null)
                    viewSurfaceRenderer.enabled = nestedTexture != null;
            }
            else if (viewSurfaceRenderer != null)
            {
                // The deepest view has no child texture, so hide this surface to
                // stop recursion cleanly instead of sampling a texture being drawn.
                viewSurfaceRenderer.enabled = false;
            }

            // The destination surface lies on the near plane. Hide its screen, but
            // leave the source portal surface available to show the nested image.
            if (LinkedPortal.viewSurfaceRenderer != null)
                LinkedPortal.viewSurfaceRenderer.enabled = false;

            for (int i = 0; i < destinationRenderers.Length; i++)
            {
                Renderer targetRenderer = destinationRenderers[i];
                if (targetRenderer == null)
                    continue;
                rendererStates[i] = targetRenderer.enabled;
                if (targetRenderer.gameObject.layer != PortalViewLayer)
                    targetRenderer.enabled = false;
            }

            camera.Render();
        }
        finally
        {
            for (int i = 0; i < destinationRenderers.Length; i++)
            {
                if (destinationRenderers[i] != null)
                    destinationRenderers[i].enabled = rendererStates[i];
            }

            if (LinkedPortal != null && LinkedPortal.viewSurfaceRenderer != null)
                LinkedPortal.viewSurfaceRenderer.enabled = destinationSurfaceWasEnabled;
            if (viewSurfaceRenderer != null)
                viewSurfaceRenderer.enabled = ownSurfaceWasEnabled;
            if (viewMaterial != null)
                viewMaterial.SetTexture(BaseMapId, previousTexture);
        }

        return recursionTextures[level];
    }

    private bool ConfigurePortalCamera(Camera camera, Camera source)
    {
        Quaternion halfTurn = Quaternion.AngleAxis(180f, Vector3.up);
        Vector3 entryLocalPosition = transform.InverseTransformPoint(source.transform.position);
        Vector3 virtualEye = LinkedPortal.transform.TransformPoint(halfTurn * entryLocalPosition);
        camera.transform.SetPositionAndRotation(virtualEye, LinkedPortal.transform.rotation);
        camera.orthographic = false;
        camera.aspect = camera.targetTexture.width / (float)camera.targetTexture.height;
        camera.farClipPlane = source.farClipPlane;
        camera.cullingMask = source.cullingMask | (1 << PortalViewLayer);

        // Bound this render to the destination aperture, not the source camera's
        // full field of view.
        BoxCollider destinationBounds = LinkedPortal.portalBounds;
        if (destinationBounds == null)
            return false;

        float halfWidth = destinationBounds.size.x * 0.45f;
        float halfHeight = destinationBounds.size.y * 0.47f;
        Vector3 lowerLeft = LinkedPortal.transform.TransformPoint(new Vector3(-halfWidth, -halfHeight, 0f));
        Vector3 lowerRight = LinkedPortal.transform.TransformPoint(new Vector3(halfWidth, -halfHeight, 0f));
        Vector3 upperLeft = LinkedPortal.transform.TransformPoint(new Vector3(-halfWidth, halfHeight, 0f));
        Vector3 portalRight = LinkedPortal.transform.right;
        Vector3 portalUp = LinkedPortal.transform.up;
        float eyeToPortal = Vector3.Dot(LinkedPortal.transform.position - virtualEye, LinkedPortal.transform.forward);
        if (eyeToPortal <= 0.001f)
            return false;

        float near = eyeToPortal + 0.01f;
        float nearScale = near / eyeToPortal;
        float left = Vector3.Dot(lowerLeft - virtualEye, portalRight) * nearScale;
        float right = Vector3.Dot(lowerRight - virtualEye, portalRight) * nearScale;
        float bottom = Vector3.Dot(lowerLeft - virtualEye, portalUp) * nearScale;
        float top = Vector3.Dot(upperLeft - virtualEye, portalUp) * nearScale;
        float far = Mathf.Max(source.farClipPlane, near + 1f);

        camera.nearClipPlane = near;
        camera.ResetProjectionMatrix();
        camera.projectionMatrix = PerspectiveOffCenter(left, right, bottom, top, near, far);
        return true;
    }

    private bool IsOnFrontSide(Camera camera)
    {
        return Vector3.Dot(camera.transform.position - transform.position, transform.forward) > 0.02f;
    }

    private bool IsInsideCameraFrustum(Camera camera)
    {
        if (viewSurfaceRenderer == null)
            return false;

        GeometryUtility.CalculateFrustumPlanes(camera, cameraFrustumPlanes);
        return GeometryUtility.TestPlanesAABB(cameraFrustumPlanes, viewSurfaceRenderer.bounds);
    }

    private Matrix4x4 PerspectiveOffCenter(float left, float right, float bottom, float top, float near, float far)
    {
        float x = 2f * near / (right - left);
        float y = 2f * near / (top - bottom);
        float a = (right + left) / (right - left);
        float b = (top + bottom) / (top - bottom);
        float c = -(far + near) / (far - near);
        float d = -(2f * far * near) / (far - near);

        Matrix4x4 projection = new Matrix4x4();
        projection[0, 0] = x;
        projection[0, 2] = a;
        projection[1, 1] = y;
        projection[1, 2] = b;
        projection[2, 2] = c;
        projection[2, 3] = d;
        projection[3, 2] = -1f;
        return projection;
    }

    private void OnTriggerEnter(Collider other)
    {
        PortalTraveller traveller = other.GetComponent<PortalTraveller>();
        if (traveller != null)
            traveller.TryTraverse(this);
    }

    private void OnTriggerStay(Collider other)
    {
        // Retry while the capsule overlaps the portal. This matters when it enters
        // the opening gradually, such as walking onto a portal placed in the floor.
        PortalTraveller traveller = other.GetComponent<PortalTraveller>();
        if (traveller != null)
            traveller.TryTraverse(this);
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
        if (viewMaterial != null)
            Destroy(viewMaterial);
        if (recursionTextures != null)
        {
            foreach (RenderTexture texture in recursionTextures)
            {
                if (texture == null)
                    continue;
                texture.Release();
                Destroy(texture);
            }
        }
        else if (viewTexture != null)
        {
            viewTexture.Release();
            Destroy(viewTexture);
        }
    }
}
