using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public sealed class ReferencePreviewToggleController : MonoBehaviour
{
    [System.Serializable]
    private struct CameraModeSettings
    {
        public bool orthographic;
        public float orthographicSize;
        public float fieldOfView;
        public Vector3 localPosition;
        public Vector3 localEulerAngles;
    }

    [SerializeField] private bool defaultReferenceMode = true;
    [SerializeField] private Behaviour referenceDriver;
    [SerializeField] private GameObject referenceRoot;
    [SerializeField] private GameObject previewRoot;
    [SerializeField] private VolumeMaterialPreviewController previewController;
    [SerializeField] private int previewMaterialCount = 1;
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private CameraModeSettings referenceCamera = new CameraModeSettings
    {
        orthographic = true,
        orthographicSize = 1.0f,
        fieldOfView = 60.0f,
        localPosition = new Vector3(0.0f, 0.0f, -1.0f),
        localEulerAngles = Vector3.zero
    };
    [SerializeField] private CameraModeSettings previewCamera = new CameraModeSettings
    {
        orthographic = false,
        orthographicSize = 1.0f,
        fieldOfView = 34.0f,
        localPosition = new Vector3(0.0f, 0.05f, -2.6f),
        localEulerAngles = new Vector3(0.0f, 0.0f, 0.0f)
    };

    private int currentMode = -1;

    private void OnEnable()
    {
        currentMode = defaultReferenceMode ? -1 : 0;
        ApplyMode();
    }

    private void Update()
    {
        if (Application.isPlaying && Input.GetKeyDown(toggleKey))
        {
            var maxPreviewIndex = Mathf.Max(0, previewMaterialCount - 1);
            currentMode++;
            if (currentMode > maxPreviewIndex)
            {
                currentMode = -1;
            }

            ApplyMode();
        }
        else if (!Application.isPlaying)
        {
            currentMode = defaultReferenceMode ? -1 : 0;
            ApplyMode();
        }
    }

    private void OnValidate()
    {
        previewMaterialCount = Mathf.Max(1, previewMaterialCount);
        currentMode = defaultReferenceMode ? -1 : 0;
        if (!isActiveAndEnabled)
        {
            return;
        }

#if UNITY_EDITOR
        EditorApplication.delayCall -= DelayedApplyMode;
        EditorApplication.delayCall += DelayedApplyMode;
#else
        ApplyMode();
#endif
    }

#if UNITY_EDITOR
    private void DelayedApplyMode()
    {
        if (this == null || !isActiveAndEnabled)
        {
            return;
        }

        ApplyMode();
    }
#endif

    private void ApplyMode()
    {
        var referenceActive = currentMode < 0;

        if (referenceRoot != null)
        {
            referenceRoot.SetActive(referenceActive);
        }

        if (previewRoot != null)
        {
            previewRoot.SetActive(!referenceActive);
        }

        if (referenceDriver != null)
        {
            referenceDriver.enabled = referenceActive;
        }

        ApplyCameraMode(referenceActive ? referenceCamera : previewCamera);

        if (!referenceActive && previewController != null)
        {
            previewController.SetMaterialIndex(Mathf.Clamp(currentMode, 0, previewMaterialCount - 1));
        }
    }

    private void ApplyCameraMode(CameraModeSettings settings)
    {
        if (targetCamera == null)
        {
            return;
        }

        var cameraTransform = targetCamera.transform;
        cameraTransform.localPosition = settings.localPosition;
        cameraTransform.localEulerAngles = settings.localEulerAngles;
        targetCamera.orthographic = settings.orthographic;
        targetCamera.orthographicSize = settings.orthographicSize;
        targetCamera.fieldOfView = settings.fieldOfView;
    }
}
