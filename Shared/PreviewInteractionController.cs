using UnityEngine;

[ExecuteAlways]
public class PreviewInteractionController : MonoBehaviour
{
    // Interaction-only component.
    // Owns runtime rotation and zoom. It must not own shape, material mode, or effect-specific shader data.
    // Effects may subclass this when they truly need different interaction behavior in a specific scene.

    [Header("Interaction")]
    [SerializeField] private bool allowMouseRotate = true;
    [SerializeField] private int mouseButton = 0;
    [SerializeField] private float rotateSpeedX = 1.05f;
    [SerializeField] private float rotateSpeedY = 0.75f;
    [SerializeField] private Vector2 pitchLimits = new Vector2(-65f, 65f);
    [SerializeField] private float rotationSmoothTime = 0.12f;
    [SerializeField] private float rotationInertiaDamping = 4.5f;
    [SerializeField] private Camera previewCamera;
    [SerializeField] private bool allowScrollZoom = true;
    [SerializeField] private float zoomSpeed = 2.4f;
    [SerializeField] private Vector2 zoomDistanceLimits = new Vector2(1.2f, 4.5f);
    [SerializeField] private bool applyTransformRotation = true;

    [SerializeField, HideInInspector] private bool adoptedLegacySettings;
    [SerializeField, HideInInspector] private bool interactionEnabled = true;

    private float pitch = 12f;
    private float yaw = -24f;
    private float targetPitch = 12f;
    private float targetYaw = -24f;
    private float pitchVelocity;
    private float yawVelocity;
    private float zoomDistance = 2.6f;
    private float targetZoomDistance = 2.6f;
    private float zoomVelocity;

    public float Pitch => pitch;
    public float Yaw => yaw;

    public void SetInteractionEnabled(bool enabled)
    {
        interactionEnabled = enabled;

        if (!interactionEnabled)
        {
            pitchVelocity = 0.0f;
            yawVelocity = 0.0f;
            zoomVelocity = 0.0f;
        }
    }

    public void AdoptLegacySettings(
        Camera legacyPreviewCamera,
        bool legacyAllowMouseRotate,
        int legacyMouseButton,
        float legacyRotateSpeedX,
        float legacyRotateSpeedY,
        Vector2 legacyPitchLimits,
        float legacyRotationSmoothTime,
        float legacyRotationInertiaDamping,
        bool legacyAllowScrollZoom,
        float legacyZoomSpeed,
        Vector2 legacyZoomDistanceLimits,
        bool legacyApplyTransformRotation)
    {
        if (adoptedLegacySettings)
        {
            return;
        }

        previewCamera = legacyPreviewCamera;
        allowMouseRotate = legacyAllowMouseRotate;
        mouseButton = legacyMouseButton;
        rotateSpeedX = legacyRotateSpeedX;
        rotateSpeedY = legacyRotateSpeedY;
        pitchLimits = legacyPitchLimits;
        rotationSmoothTime = legacyRotationSmoothTime;
        rotationInertiaDamping = legacyRotationInertiaDamping;
        allowScrollZoom = legacyAllowScrollZoom;
        zoomSpeed = legacyZoomSpeed;
        zoomDistanceLimits = legacyZoomDistanceLimits;
        applyTransformRotation = legacyApplyTransformRotation;
        adoptedLegacySettings = true;
    }

    public void Tick(Transform previewTransform)
    {
        HandleMouseRotate(previewTransform);
    }

    public void CacheCameraDefaults()
    {
        var cam = ResolvePreviewCamera();
        if (cam == null)
        {
            return;
        }

        zoomDistance = Mathf.Clamp(Mathf.Abs(cam.transform.localPosition.z), zoomDistanceLimits.x, zoomDistanceLimits.y);
        targetZoomDistance = zoomDistance;
    }

    public float GetPreviewZoomDistance()
    {
        return targetZoomDistance;
    }

    public void SetPreviewZoomDistance(float distance)
    {
        var clamped = Mathf.Clamp(distance, zoomDistanceLimits.x, zoomDistanceLimits.y);
        zoomDistance = clamped;
        targetZoomDistance = clamped;
        zoomVelocity = 0.0f;

        var cam = ResolvePreviewCamera();
        if (!interactionEnabled || cam == null || cam.orthographic)
        {
            return;
        }

        var local = cam.transform.localPosition;
        local.z = -clamped;
        cam.transform.localPosition = local;
    }

    private void HandleMouseRotate(Transform previewTransform)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!interactionEnabled)
        {
            return;
        }

        if (allowMouseRotate && Input.GetMouseButton(mouseButton))
        {
            targetPitch = Mathf.Clamp(
                targetPitch - Input.GetAxis("Mouse Y") * rotateSpeedY * 20.0f,
                pitchLimits.x,
                pitchLimits.y
            );
            targetYaw += Input.GetAxis("Mouse X") * rotateSpeedX * 20.0f;
        }

        pitch = Mathf.SmoothDampAngle(pitch, targetPitch, ref pitchVelocity, rotationSmoothTime);
        yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVelocity, rotationSmoothTime);

        if (!Input.GetMouseButton(mouseButton))
        {
            var damping = Mathf.Exp(-rotationInertiaDamping * Mathf.Max(Time.unscaledDeltaTime, 0.0001f));
            targetPitch += pitchVelocity * Time.unscaledDeltaTime;
            targetYaw += yawVelocity * Time.unscaledDeltaTime;
            pitchVelocity *= damping;
            yawVelocity *= damping;
            targetPitch = Mathf.Clamp(targetPitch, pitchLimits.x, pitchLimits.y);
        }

        previewTransform.rotation = applyTransformRotation
            ? Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, Vector3.right)
            : Quaternion.identity;

        HandleScrollZoom();
    }

    private void HandleScrollZoom()
    {
        if (!interactionEnabled || !allowScrollZoom)
        {
            return;
        }

        var cam = ResolvePreviewCamera();
        if (cam == null || cam.orthographic)
        {
            return;
        }

        var scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 1e-4f)
        {
            targetZoomDistance = Mathf.Clamp(targetZoomDistance - scroll * zoomSpeed, zoomDistanceLimits.x, zoomDistanceLimits.y);
        }

        zoomDistance = Mathf.SmoothDamp(zoomDistance, targetZoomDistance, ref zoomVelocity, 0.08f);
        var local = cam.transform.localPosition;
        local.z = -zoomDistance;
        cam.transform.localPosition = local;
    }

    private Camera ResolvePreviewCamera()
    {
        if (previewCamera != null)
        {
            return previewCamera;
        }

        if (Camera.main != null)
        {
            previewCamera = Camera.main;
        }

        return previewCamera;
    }
}
