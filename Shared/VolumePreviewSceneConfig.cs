using UnityEngine;

[ExecuteAlways]
public sealed class VolumePreviewSceneConfig : MonoBehaviour
{
    [SerializeField] private VolumePreviewSceneProfile sceneProfile;
    [SerializeField] private ReferencePreviewToggleController toggleController;
    [SerializeField] private VolumeMaterialPreviewController previewController;

    private void OnEnable()
    {
        Apply();
    }

    private void OnValidate()
    {
        ResolveReferences();
        Apply();
    }

    public void Apply()
    {
        ResolveReferences();

        if (toggleController != null)
        {
            toggleController.SetSceneProfile(sceneProfile);
        }

        if (previewController != null)
        {
            previewController.SetSceneProfile(sceneProfile);
        }
    }

    private void ResolveReferences()
    {
        if (toggleController == null)
        {
            toggleController = GetComponent<ReferencePreviewToggleController>();
        }

        if (previewController == null)
        {
            previewController = GetComponentInChildren<VolumeMaterialPreviewController>(true);
        }
    }
}
