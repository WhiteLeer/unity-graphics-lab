using UnityEngine;

[ExecuteAlways]
public class PreviewInteractionController : MonoBehaviour
{
    // Interaction-only component.
    // Owns runtime rotation and zoom. It must not own shape, material mode, or effect-specific shader data.
    // Effects may subclass this when they truly need different interaction behavior in a specific scene.

    [Header("交互")]
    [SerializeField, InspectorName("允许鼠标旋转")] private bool allowMouseRotate = true;
    [SerializeField, InspectorName("鼠标按键")] private int mouseButton = 0;
    [SerializeField, InspectorName("横向旋转速度")] private float rotateSpeedX = 1.05f;
    [SerializeField, InspectorName("纵向旋转速度")] private float rotateSpeedY = 0.75f;
    [SerializeField, InspectorName("俯仰角限制")] private Vector2 pitchLimits = new Vector2(-65f, 65f);
    [SerializeField, InspectorName("旋转平滑时间")] private float rotationSmoothTime = 0.12f;
    [SerializeField, InspectorName("旋转惯性衰减")] private float rotationInertiaDamping = 4.5f;
    [SerializeField, InspectorName("预览相机")] private Camera previewCamera;
    [SerializeField, InspectorName("允许滚轮缩放")] private bool allowScrollZoom = true;
    [SerializeField, InspectorName("缩放速度")] private float zoomSpeed = 2.4f;
    [SerializeField, InspectorName("缩放范围")] private Vector2 zoomDistanceLimits = new Vector2(1.2f, 4.5f);
    // 保留旧序列化字段，兼容已有场景；旋转现在始终由相机轨道承担。
    [SerializeField, HideInInspector] private bool applyTransformRotation = true;

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
    public Camera PreviewCamera => ResolvePreviewCamera();

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
        Tick(previewTransform != null ? previewTransform.position : Vector3.zero);
    }

    public void Tick(Vector3 orbitTargetPosition)
    {
        HandleMouseRotate(orbitTargetPosition);
    }

    public void CacheCameraDefaults(Transform orbitTarget = null)
    {
        var cam = ResolvePreviewCamera();
        if (cam == null)
        {
            return;
        }

        var target = orbitTarget != null ? orbitTarget.position : Vector3.zero;
        CacheCameraDefaults(target);
    }

    public void CacheCameraDefaults(Vector3 orbitTargetPosition)
    {
        var cam = ResolvePreviewCamera();
        if (cam == null)
        {
            return;
        }

        var target = orbitTargetPosition;
        var offset = cam.transform.position - target;
        var distance = Mathf.Max(offset.magnitude, 0.0001f);
        zoomDistance = Mathf.Clamp(distance, zoomDistanceLimits.x, zoomDistanceLimits.y);
        targetZoomDistance = zoomDistance;

        var direction = offset / distance;
        yaw = targetYaw = Mathf.Atan2(direction.x, -direction.z) * Mathf.Rad2Deg;
        pitch = targetPitch = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
        pitch = targetPitch = Mathf.Clamp(pitch, pitchLimits.x, pitchLimits.y);
        pitchVelocity = 0f;
        yawVelocity = 0f;
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

        // 相机位置在下一帧根据当前预览目标重新计算，避免写死相机的 local Z。
    }

    public void FramePreview(Vector3 orbitTargetPosition, float distance)
    {
        var clamped = Mathf.Clamp(distance, zoomDistanceLimits.x, zoomDistanceLimits.y);
        zoomDistance = clamped;
        targetZoomDistance = clamped;
        zoomVelocity = 0.0f;
        ApplyCameraOrbit(orbitTargetPosition);
    }

    private void HandleMouseRotate(Vector3 orbitTargetPosition)
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

        HandleScrollZoom();
        ApplyCameraOrbit(orbitTargetPosition);
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
        // 位置由 ApplyCameraOrbit 统一写入，滚轮只改变轨道半径。
    }

    private void ApplyCameraOrbit(Vector3 orbitTargetPosition)
    {
        var cam = ResolvePreviewCamera();
        if (cam == null || cam.orthographic)
        {
            return;
        }

        var distance = Mathf.Clamp(zoomDistance, zoomDistanceLimits.x, zoomDistanceLimits.y);
        // 交互角度沿用旧的物体旋转语义；相机绕目标旋转时需要取逆变换。
        var orbitRotation = Quaternion.AngleAxis(-yaw, Vector3.up) * Quaternion.AngleAxis(-pitch, Vector3.right);
        var target = orbitTargetPosition;
        cam.transform.position = target + orbitRotation * new Vector3(0f, 0f, -distance);

        var direction = target - cam.transform.position;
        if (direction.sqrMagnitude > 0.000001f)
        {
            cam.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }
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
