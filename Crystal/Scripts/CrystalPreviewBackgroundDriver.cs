using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public sealed class CrystalPreviewBackgroundDriver : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float distanceFromCamera = 6.0f;
    [SerializeField] private float widthScale = 1.08f;
    [SerializeField] private float heightScale = 1.08f;

    private MeshFilter meshFilter;

    private void OnEnable()
    {
        Initialize();
        Apply();
    }

    private void Update()
    {
        Initialize();
        Apply();
    }

    private void OnValidate()
    {
        distanceFromCamera = Mathf.Max(0.1f, distanceFromCamera);
        widthScale = Mathf.Max(0.1f, widthScale);
        heightScale = Mathf.Max(0.1f, heightScale);

        if (!isActiveAndEnabled)
        {
            return;
        }

        Apply();
    }

    public void ForceApply()
    {
        Initialize();
        Apply();
    }

    private void Initialize()
    {
        if (meshFilter == null)
        {
            meshFilter = GetComponent<MeshFilter>();
        }

        if (meshFilter.sharedMesh == null)
        {
            meshFilter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        }
    }

    private void Apply()
    {
        if (targetCamera == null)
        {
            return;
        }

        var cameraTransform = targetCamera.transform;
        var transformToDrive = transform;
        transformToDrive.position = cameraTransform.position + cameraTransform.forward * distanceFromCamera;
        transformToDrive.rotation = cameraTransform.rotation;

        var height = targetCamera.orthographic
            ? targetCamera.orthographicSize * 2.0f
            : 2.0f * distanceFromCamera * Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        var width = height * targetCamera.aspect;
        transformToDrive.localScale = new Vector3(width * widthScale, height * heightScale, 1.0f);
    }
}
