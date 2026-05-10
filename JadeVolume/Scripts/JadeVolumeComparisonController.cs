using UnityEngine;

[ExecuteAlways]
public sealed class JadeVolumeComparisonController : MonoBehaviour
{
    private enum ComparisonMode
    {
        ReferencePlane = 0,
        VolumeObject = 1
    }

    [SerializeField, InspectorName("默认显示")] private ComparisonMode defaultMode = ComparisonMode.ReferencePlane;
    [SerializeField, InspectorName("参考版对象")] private GameObject referencePlane;
    [SerializeField, InspectorName("物体版对象")] private GameObject volumeObject;
    [SerializeField, InspectorName("切换按键")] private KeyCode toggleKey = KeyCode.Tab;

    private ComparisonMode currentMode;

    private void OnEnable()
    {
        currentMode = defaultMode;
        ApplyMode();
    }

    private void Update()
    {
        if (Application.isPlaying && Input.GetKeyDown(toggleKey))
        {
            currentMode = currentMode == ComparisonMode.ReferencePlane
                ? ComparisonMode.VolumeObject
                : ComparisonMode.ReferencePlane;
            ApplyMode();
        }
        else if (!Application.isPlaying)
        {
            currentMode = defaultMode;
            ApplyMode();
        }
    }

    private void OnValidate()
    {
        currentMode = defaultMode;
    }

    private void ApplyMode()
    {
        if (referencePlane != null)
        {
            referencePlane.SetActive(currentMode == ComparisonMode.ReferencePlane);
        }

        if (volumeObject != null)
        {
            volumeObject.SetActive(currentMode == ComparisonMode.VolumeObject);
        }
    }
}
