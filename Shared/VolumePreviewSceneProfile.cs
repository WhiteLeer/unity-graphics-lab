using UnityEngine;

[CreateAssetMenu(menuName = "Unity Graphics Lab/Volume Preview Scene Profile", fileName = "VolumePreviewSceneProfile")]
public sealed class VolumePreviewSceneProfile : ScriptableObject
{
    [System.Serializable]
    public struct PreviewModeDefinition
    {
        [SerializeField, InspectorName("挡位名称")] private string displayName;
        [SerializeField, InspectorName("预览材质")] private Material previewMaterial;
        [SerializeField, InspectorName("预览网格")] private Mesh previewMesh;
        [SerializeField, InspectorName("使用全屏四边形")] private bool useFullscreenQuad;

        public string DisplayName => displayName;
        public Material PreviewMaterial => previewMaterial;
        public Mesh PreviewMesh => previewMesh;
        public bool UseFullscreenQuad => useFullscreenQuad;
    }

    [System.Serializable]
    public struct CameraDefinition
    {
        [SerializeField, InspectorName("清除模式")] private CameraClearFlags clearFlags;
        [SerializeField, InspectorName("正交")] private bool orthographic;
        [SerializeField, InspectorName("正交大小")] private float orthographicSize;
        [SerializeField, InspectorName("视野角")] private float fieldOfView;
        [SerializeField, InspectorName("本地位置")] private Vector3 localPosition;
        [SerializeField, InspectorName("本地旋转")] private Vector3 localEulerAngles;
        [SerializeField, InspectorName("近平面")] private float nearClipPlane;
        [SerializeField, InspectorName("远平面")] private float farClipPlane;

        public void ApplyTo(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            camera.clearFlags = clearFlags;
            camera.orthographic = orthographic;
            camera.orthographicSize = orthographicSize;
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = nearClipPlane;
            camera.farClipPlane = farClipPlane;

            var transform = camera.transform;
            transform.localPosition = localPosition;
            transform.localEulerAngles = localEulerAngles;
        }
    }

    [Header("挡位默认值")]
    [SerializeField, InspectorName("切换键")] private KeyCode toggleKey = KeyCode.Tab;

    [Header("场景外观")]
    [SerializeField, InspectorName("相机背景色")] private Color cameraBackgroundColor = Color.black;
    [SerializeField, InspectorName("相机默认值")] private CameraDefinition sceneCameraDefaults;

    [Header("预览挡位")]
    [SerializeField, InspectorName("挡位列表")] private PreviewModeDefinition[] previewModes = System.Array.Empty<PreviewModeDefinition>();

    [Header("交互默认值")]
    [SerializeField, InspectorName("允许鼠标旋转")] private bool allowMouseRotate = true;
    [SerializeField, InspectorName("鼠标按键")] private int mouseButton = 0;
    [SerializeField, InspectorName("横向旋转速度")] private float rotateSpeedX = 1.05f;
    [SerializeField, InspectorName("纵向旋转速度")] private float rotateSpeedY = 0.75f;
    [SerializeField, InspectorName("俯仰角限制")] private Vector2 pitchLimits = new Vector2(-65f, 65f);
    [SerializeField, InspectorName("旋转平滑时间")] private float rotationSmoothTime = 0.12f;
    [SerializeField, InspectorName("旋转惯性衰减")] private float rotationInertiaDamping = 4.5f;
    [SerializeField, InspectorName("允许滚轮缩放")] private bool allowScrollZoom = true;
    [SerializeField, InspectorName("缩放速度")] private float zoomSpeed = 2.4f;
    [SerializeField, InspectorName("缩放范围")] private Vector2 zoomDistanceLimits = new Vector2(1.2f, 4.5f);
    [SerializeField, InspectorName("把旋转写到物体")] private bool applyTransformRotation = true;
    [SerializeField, InspectorName("允许运行时切形状")] private bool allowRuntimeShapeSwitch = true;
    [SerializeField, InspectorName("切形状键")] private KeyCode cycleShapeKey = KeyCode.None;

    public int PreviewModeCount => previewModes != null ? previewModes.Length : 0;
    public Color CameraBackgroundColor => cameraBackgroundColor;
    public CameraDefinition SceneCameraDefaults => sceneCameraDefaults;
    public bool AllowMouseRotate => allowMouseRotate;
    public KeyCode ToggleKey => toggleKey;
    public int MouseButton => mouseButton;
    public float RotateSpeedX => rotateSpeedX;
    public float RotateSpeedY => rotateSpeedY;
    public Vector2 PitchLimits => pitchLimits;
    public float RotationSmoothTime => rotationSmoothTime;
    public float RotationInertiaDamping => rotationInertiaDamping;
    public bool AllowScrollZoom => allowScrollZoom;
    public float ZoomSpeed => zoomSpeed;
    public Vector2 ZoomDistanceLimits => zoomDistanceLimits;
    public bool ApplyTransformRotation => applyTransformRotation;
    public bool AllowRuntimeShapeSwitch => allowRuntimeShapeSwitch;
    public KeyCode CycleShapeKey => cycleShapeKey;

    public Material GetPreviewMaterial(int index)
    {
        if (previewModes == null || previewModes.Length == 0)
        {
            return null;
        }

        index = Mathf.Clamp(index, 0, previewModes.Length - 1);
        return previewModes[index].PreviewMaterial;
    }

    public Mesh GetPreviewMesh(int index)
    {
        if (previewModes == null || previewModes.Length == 0)
        {
            return null;
        }

        index = Mathf.Clamp(index, 0, previewModes.Length - 1);
        return previewModes[index].PreviewMesh;
    }

    public bool IsPreviewModeSpecial(int index)
    {
        if (previewModes == null || previewModes.Length == 0)
        {
            return false;
        }

        index = Mathf.Clamp(index, 0, previewModes.Length - 1);
        return previewModes[index].UseFullscreenQuad;
    }

    public Material[] GetPreviewMaterials()
    {
        if (previewModes == null || previewModes.Length == 0)
        {
            return System.Array.Empty<Material>();
        }

        var materials = new Material[previewModes.Length];
        for (var i = 0; i < previewModes.Length; i++)
        {
            materials[i] = previewModes[i].PreviewMaterial;
        }

        return materials;
    }
}
