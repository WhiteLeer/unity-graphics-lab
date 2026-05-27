using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class ColorGradingRenderFeature : ScriptableRendererFeature
{
    public enum ToneMappingMode
    {
        None = 0,
        Reinhard = 1,
        ACES = 2
    }

    [System.Serializable]
    public class Settings
    {
        [Header("Tone Mapping")]
        public ToneMappingMode toneMapping = ToneMappingMode.ACES;

        [Range(0.1f, 5.0f)]
        public float exposure = 1.0f;

        [Header("Color Grading")]
        [Range(-100f, 100f)]
        public float postExposureEV = 0.0f;

        [Range(0.0f, 2.0f)]
        public float contrast = 1.0f;

        [Range(0.0f, 2.0f)]
        public float saturation = 1.0f;

        [Range(-180f, 180f)]
        public float hueShift = 0.0f;

        [Range(-100f, 100f)]
        public float temperature = 0.0f;

        [Range(-100f, 100f)]
        public float tint = 0.0f;

        [ColorUsage(false, true)]
        public Color colorFilter = Color.white;

        [Header("Rendering")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public Settings settings = new Settings();

    private ColorGradingRenderPass m_Pass;
    private Material m_Material;

    public override void Create()
    {
        Shader shader = Shader.Find("Hidden/Func8/ColorGradingToneMapping");
        if (shader == null)
        {
            Debug.LogError("Color grading shader not found: Hidden/Func8/ColorGradingToneMapping");
            return;
        }

        if (m_Material != null)
            CoreUtils.Destroy(m_Material);

        m_Material = CoreUtils.CreateEngineMaterial(shader);
        m_Pass = new ColorGradingRenderPass(m_Material, settings);
        m_Pass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (m_Material == null || m_Pass == null)
            return;

        if (renderingData.cameraData.cameraType != CameraType.Game &&
            renderingData.cameraData.cameraType != CameraType.SceneView)
            return;

        m_Pass.renderPassEvent = settings.renderPassEvent;
        m_Pass.Setup(renderer);
        renderer.EnqueuePass(m_Pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(m_Material);
    }
}
