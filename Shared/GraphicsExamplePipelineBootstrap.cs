using UnityEngine;
using UnityEngine.Rendering;

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
    }
}
