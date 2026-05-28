using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(Renderer))]
public sealed class JadeVolumeController : MonoBehaviour
{
    // Layout-only helper for the jade scene.
    // This script is NOT part of preview ownership for:
    // - preview mode switching
    // - preview material selection
    // - preview shape
    // - preview zoom / rotation
    //
    // It only keeps the presentation canvas in a fixed layout.
    // Do not add preview state writes here; use JadeVolumePreviewController / ReferencePreviewToggleController instead.
    [Header("画布显示")]
    [SerializeField, InspectorName("自动保持画布布局")] private bool lockCanvasLayout = true;
    [SerializeField, InspectorName("画布宽度")] private float canvasWidth = 16f;
    [SerializeField, InspectorName("画布高度")] private float canvasHeight = 9f;
    [SerializeField, InspectorName("画布厚度")] private float canvasDepth = 0.1f;
    [SerializeField, InspectorName("高度偏移")] private float canvasOffsetY = 1.45f;
    [SerializeField, InspectorName("前后位置")] private float canvasOffsetZ = 0.2f;

    private MeshFilter meshFilter = null!;

    private void OnEnable()
    {
        Initialize();
        Apply();
    }

    private void Update()
    {
        Apply();
    }

    private void OnValidate()
    {
        canvasWidth = Mathf.Max(0.1f, canvasWidth);
        canvasHeight = Mathf.Max(0.1f, canvasHeight);
        canvasDepth = Mathf.Max(0.01f, canvasDepth);

        if (isActiveAndEnabled)
        {
            Apply();
        }
    }

    private void Initialize()
    {
        if (meshFilter == null)
        {
            meshFilter = GetComponent<MeshFilter>();
        }

        if (meshFilter.sharedMesh == null || meshFilter.sharedMesh.name.Contains("Runtime Mesh"))
        {
            meshFilter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        }
    }

    private void Apply()
    {
        Initialize();

        if (!lockCanvasLayout)
        {
            return;
        }

        transform.localPosition = new Vector3(0f, canvasOffsetY, canvasOffsetZ);
        transform.localRotation = Quaternion.identity;
        transform.localScale = new Vector3(canvasWidth, canvasHeight, canvasDepth);
    }
}
