using UnityEngine;

[ExecuteAlways]
public sealed class JadeVolumeComparisonController : MonoBehaviour
{
    private enum ComparisonMode
    {
        ReferencePlane = 0,
        VolumeObject = 1,
        VolumeObjectSimpleJade = 2
    }

    [SerializeField, InspectorName("默认显示")] private ComparisonMode defaultMode = ComparisonMode.ReferencePlane;
    [SerializeField, InspectorName("参考版对象")] private GameObject referencePlane;
    [SerializeField, InspectorName("物体版对象")] private GameObject volumeObject;
    [SerializeField, InspectorName("切换按键")] private KeyCode toggleKey = KeyCode.Tab;

    private ComparisonMode currentMode;
    private JadeVolumeVolumeController volumeController;

    private void OnEnable()
    {
        CacheReferences();
        currentMode = defaultMode;
        ApplyMode();
    }

    private void Update()
    {
        if (Application.isPlaying && Input.GetKeyDown(toggleKey))
        {
            currentMode = (ComparisonMode)(((int)currentMode + 1) % 3);
            ApplyMode();
        }
        else if (!Application.isPlaying)
        {
            CacheReferences();
            currentMode = defaultMode;
            ApplyMode();
        }
    }

    private void OnValidate()
    {
        CacheReferences();
        currentMode = defaultMode;
    }

    private void CacheReferences()
    {
        volumeController = volumeObject != null ? volumeObject.GetComponent<JadeVolumeVolumeController>() : null;
    }

    private void ApplyMode()
    {
        if (referencePlane != null)
        {
            referencePlane.SetActive(currentMode == ComparisonMode.ReferencePlane);
        }

        if (volumeObject != null)
        {
            var isVolumeMode = currentMode == ComparisonMode.VolumeObject || currentMode == ComparisonMode.VolumeObjectSimpleJade;
            volumeObject.SetActive(isVolumeMode);
        }

        if (volumeController != null)
        {
            var materialMode = currentMode == ComparisonMode.VolumeObjectSimpleJade
                ? JadeVolumeVolumeController.PreviewMaterialMode.SimpleJade
                : JadeVolumeVolumeController.PreviewMaterialMode.Default;
            volumeController.SetPreviewMaterialMode(materialMode);
        }
    }
}
