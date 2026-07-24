using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[ExecuteAlways]
public sealed class GraphicsExamplePipelineBootstrap : MonoBehaviour
{
    [SerializeField] private RenderPipelineAsset pipelineAsset;
    [SerializeField] private RenderPipelineAsset alternatePipelineAsset;
    [SerializeField] private bool allowRuntimeToggle = false;
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;
    [SerializeField] private bool useAlternateAtStart = false;

    private bool useAlternate;

    private void OnEnable()
    {
        useAlternate = useAlternateAtStart;
        Apply();
    }

    private void Start()
    {
        // QualitySettings can reapply its pipeline when Play mode starts.
        // Apply once after that transition so the Game view uses this scene's pipeline.
        Apply();
    }

    private void OnValidate()
    {
        Apply();
    }

    private void Update()
    {
        if (!Application.isPlaying || !allowRuntimeToggle || alternatePipelineAsset == null)
        {
            return;
        }

        if (Input.GetKeyDown(toggleKey))
        {
            useAlternate = !useAlternate;
            Apply();
        }
    }

    private void Apply()
    {
        var target = useAlternate && alternatePipelineAsset != null
            ? alternatePipelineAsset
            : pipelineAsset;

        if (target == null)
        {
            return;
        }

        GraphicsSettings.defaultRenderPipeline = target;
        QualitySettings.renderPipeline = target;

        if (Application.isPlaying && Camera.main != null)
        {
            var cameraData = Camera.main.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
        }
    }
}
