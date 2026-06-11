using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public sealed class ReferencePreviewToggleController : MonoBehaviour
{
    // Ownership contract for reference/preview mode switching:
    // 1. This class owns only currentMode and per-mode zoom persistence.
    // 2. It may activate/deactivate roots, apply camera presets, switch preview material index,
    //    and restore zoom for the selected preview mode.
    // 3. It must not own preview shape, shader property values, or per-material authoring data.
    // 4. If mode switching seems to need shape changes, route that decision through the preview controller API
    //    instead of writing scene/material state here.
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
    [SerializeField] private GameObject previewBackdropRoot;
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

    [SerializeField] private int currentMode = -1;
    [SerializeField] private float[] previewZoomDistances = new float[0];
    private int lastAppliedMode = int.MinValue;

    private void OnEnable()
    {
        EnsurePreviewZoomStorage();
        currentMode = ClampMode(currentMode);
        lastAppliedMode = int.MinValue;
#if UNITY_EDITOR
        EditorApplication.update -= EditorTick;
        EditorApplication.update += EditorTick;
#endif
        ApplyMode();
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= EditorTick;
#endif
    }

    private void Update()
    {
        TickModeSwitch();
    }

#if UNITY_EDITOR
    private void EditorTick()
    {
        if (Application.isPlaying || this == null || !isActiveAndEnabled)
        {
            return;
        }

        TickModeSwitch();
    }
#endif

    private void TickModeSwitch()
    {
        if (Application.isPlaying && Input.GetKeyDown(toggleKey))
        {
            currentMode++;
            if (currentMode > Mathf.Max(0, previewMaterialCount - 1))
            {
                currentMode = -1;
            }
        }

        currentMode = ClampMode(currentMode);
        if (currentMode != lastAppliedMode || !IsAppliedStateConsistent())
        {
            ApplyMode();
        }
    }

    private void OnValidate()
    {
        previewMaterialCount = Mathf.Max(1, previewMaterialCount);
        EnsurePreviewZoomStorage();
        currentMode = ClampMode(currentMode);
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

        // Persist zoom per preview material mode before we switch away.
        if (previewController != null && lastAppliedMode >= 0 && lastAppliedMode < previewMaterialCount)
        {
            previewZoomDistances[lastAppliedMode] = previewController.GetPreviewZoomDistance();
        }

        if (referenceRoot != null)
        {
            referenceRoot.SetActive(referenceActive);
        }

        if (previewRoot != null)
        {
            previewRoot.SetActive(!referenceActive);
        }

        if (previewBackdropRoot != null)
        {
            previewBackdropRoot.SetActive(!referenceActive);
        }

        if (referenceDriver != null)
        {
            referenceDriver.enabled = referenceActive;
        }

        ApplyCameraMode(referenceActive ? referenceCamera : previewCamera);

        if (!referenceActive && previewController != null)
        {
            var previewMode = Mathf.Clamp(currentMode, 0, previewMaterialCount - 1);
            previewController.SetMaterialIndex(previewMode);

            // Restore the preview controller's zoom for this specific mode only.
            var storedZoom = previewZoomDistances[previewMode];
            if (storedZoom > 0.0f)
            {
                previewController.SetPreviewZoomDistance(storedZoom);
            }
        }

        lastAppliedMode = currentMode;
    }

    public void SetMode(int mode)
    {
        currentMode = ClampMode(mode);
        ApplyMode();
    }

    public void AdvanceMode()
    {
        currentMode++;
        if (currentMode > Mathf.Max(0, previewMaterialCount - 1))
        {
            currentMode = -1;
        }

        currentMode = ClampMode(currentMode);
        ApplyMode();
    }

    private int ClampMode(int mode)
    {
        return Mathf.Clamp(mode, -1, Mathf.Max(0, previewMaterialCount - 1));
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

    private void EnsurePreviewZoomStorage()
    {
        var desiredCount = Mathf.Max(1, previewMaterialCount);
        if (previewZoomDistances != null && previewZoomDistances.Length == desiredCount)
        {
            InitializeMissingPreviewZooms();
            return;
        }

        var resized = new float[desiredCount];
        if (previewZoomDistances != null)
        {
            for (var i = 0; i < Mathf.Min(previewZoomDistances.Length, desiredCount); i++)
            {
                resized[i] = previewZoomDistances[i];
            }
        }

        previewZoomDistances = resized;
        InitializeMissingPreviewZooms();
    }

    private void InitializeMissingPreviewZooms()
    {
        var defaultZoom = Mathf.Abs(previewCamera.localPosition.z);
        if (defaultZoom <= 0.0f)
        {
            defaultZoom = 2.6f;
        }

        for (var i = 0; i < previewZoomDistances.Length; i++)
        {
            if (previewZoomDistances[i] <= 0.0f)
            {
                previewZoomDistances[i] = defaultZoom * (1.0f + 0.12f * i);
            }
        }
    }

    private bool IsAppliedStateConsistent()
    {
        var referenceActive = currentMode < 0;

        if (referenceRoot != null && referenceRoot.activeSelf != referenceActive)
        {
            return false;
        }

        if (previewRoot != null && previewRoot.activeSelf == referenceActive)
        {
            return false;
        }

        if (previewBackdropRoot != null && previewBackdropRoot.activeSelf == referenceActive)
        {
            return false;
        }

        if (referenceDriver != null && referenceDriver.enabled != referenceActive)
        {
            return false;
        }

        if (targetCamera != null)
        {
            if (referenceActive)
            {
                if (targetCamera.orthographic != referenceCamera.orthographic)
                {
                    return false;
                }

                if (!Approximately(targetCamera.transform.localPosition, referenceCamera.localPosition))
                {
                    return false;
                }
            }
            else
            {
                if (targetCamera.orthographic != previewCamera.orthographic)
                {
                    return false;
                }

                if (!Mathf.Approximately(targetCamera.transform.localPosition.x, previewCamera.localPosition.x) ||
                    !Mathf.Approximately(targetCamera.transform.localPosition.y, previewCamera.localPosition.y))
                {
                    return false;
                }

                if (!Mathf.Approximately(targetCamera.fieldOfView, previewCamera.fieldOfView))
                {
                    return false;
                }
            }
        }

        if (!referenceActive && previewController != null)
        {
            var previewMode = Mathf.Clamp(currentMode, 0, previewMaterialCount - 1);
            if (previewController.GetMaterialIndex() != previewMode)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Approximately(Vector3 a, Vector3 b)
    {
        return Mathf.Approximately(a.x, b.x) &&
               Mathf.Approximately(a.y, b.y) &&
               Mathf.Approximately(a.z, b.z);
    }
}
