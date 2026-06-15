using UnityEngine;

[CreateAssetMenu(menuName = "Unity Graphics Lab/Volume Preview Scene Profile", fileName = "VolumePreviewSceneProfile")]
public sealed class VolumePreviewSceneProfile : ScriptableObject
{
    [System.Serializable]
    public struct PreviewModeDefinition
    {
        [SerializeField] private string displayName;
        [SerializeField] private Material previewMaterial;

        public string DisplayName => displayName;
        public Material PreviewMaterial => previewMaterial;
    }

    [Header("Mode Defaults")]
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;

    [Header("Preview Modes")]
    [SerializeField] private PreviewModeDefinition[] previewModes = System.Array.Empty<PreviewModeDefinition>();

    [Header("Interaction Defaults")]
    [SerializeField] private bool allowMouseRotate = true;
    [SerializeField] private int mouseButton = 0;
    [SerializeField] private float rotateSpeedX = 1.05f;
    [SerializeField] private float rotateSpeedY = 0.75f;
    [SerializeField] private Vector2 pitchLimits = new Vector2(-65f, 65f);
    [SerializeField] private float rotationSmoothTime = 0.12f;
    [SerializeField] private float rotationInertiaDamping = 4.5f;
    [SerializeField] private bool allowScrollZoom = true;
    [SerializeField] private float zoomSpeed = 2.4f;
    [SerializeField] private Vector2 zoomDistanceLimits = new Vector2(1.2f, 4.5f);
    [SerializeField] private bool applyTransformRotation = true;
    [SerializeField] private bool allowRuntimeShapeSwitch = true;
    [SerializeField] private KeyCode cycleShapeKey = KeyCode.None;

    public int PreviewModeCount => previewModes != null ? previewModes.Length : 0;
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
