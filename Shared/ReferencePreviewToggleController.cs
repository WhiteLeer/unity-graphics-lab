using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public sealed class ReferencePreviewToggleController : MonoBehaviour
{
    // Ownership contract for preview mode switching:
    // 1. This class owns only currentMode and background color.
    // 2. It must not own preview mesh, root activation, camera pose, zoom, shader property values, or per-material authoring data.
    // 3. Camera TRS lives on the scene template / camera object itself.
    // 4. Scroll zoom is owned by PreviewInteractionController.

    [SerializeField] private VolumeMaterialPreviewController previewController;
    [HideInInspector]
    [SerializeField] private VolumePreviewSceneProfile templateProfile;
    [HideInInspector]
    [SerializeField] private int previewMaterialCount = 1;
    [HideInInspector]
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;
    [SerializeField] private Camera targetCamera;

    [SerializeField] private int currentMode = 0;
    private int lastAppliedMode = int.MinValue;

    private void OnEnable()
    {
        ApplyTemplateProfileDefaults();
        SyncPreviewMaterialCount();
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
                currentMode = 0;
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
        ApplyTemplateProfileDefaults();
        SyncPreviewMaterialCount();
        previewMaterialCount = Mathf.Max(1, previewMaterialCount);
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
        ApplySceneBackgroundColor();

        if (previewController != null)
        {
            previewController.SetMaterialIndex(currentMode);
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
            currentMode = 0;
        }

        currentMode = ClampMode(currentMode);
        ApplyMode();
    }

    private int ClampMode(int mode)
    {
        return Mathf.Clamp(mode, 0, Mathf.Max(0, previewMaterialCount - 1));
    }

    private void ApplyTemplateProfileDefaults()
    {
        if (templateProfile == null)
        {
            return;
        }

        toggleKey = templateProfile.ToggleKey;
    }

    private void SyncPreviewMaterialCount()
    {
        if (templateProfile != null && templateProfile.PreviewModeCount > 0)
        {
            previewMaterialCount = Mathf.Max(1, templateProfile.PreviewModeCount);
            return;
        }

        if (previewController == null)
        {
            return;
        }

        previewMaterialCount = Mathf.Max(1, previewController.GetPreviewMaterialCount());
    }

    public void SetSceneProfile(VolumePreviewSceneProfile profile)
    {
        templateProfile = profile;
        ApplyTemplateProfileDefaults();
        SyncPreviewMaterialCount();

        if (isActiveAndEnabled)
        {
            ApplyMode();
        }
    }

    private void ApplySceneBackgroundColor()
    {
        if (targetCamera == null || templateProfile == null)
        {
            return;
        }

        targetCamera.backgroundColor = templateProfile.CameraBackgroundColor;
    }

    private bool IsAppliedStateConsistent()
    {
        if (targetCamera != null)
        {
            if (templateProfile != null && targetCamera.backgroundColor != templateProfile.CameraBackgroundColor)
            {
                return false;
            }
        }

        if (previewController != null)
        {
            if (previewController.GetMaterialIndex() != currentMode)
            {
                return false;
            }
        }

        return true;
    }
}
