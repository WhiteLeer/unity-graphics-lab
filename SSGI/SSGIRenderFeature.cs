using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class SSGIRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("SSGI")]
        [Range(0f, 4f)]
        public float intensity = 1.0f;

        [Range(0.5f, 30f)]
        public float rayLength = 8.0f;

        [Range(0.01f, 2f)]
        public float thickness = 0.25f;

        [Range(0.0f, 0.2f)]
        public float rayBias = 0.03f;

        [Range(1, 32)]
        public int sampleCount = 4;

        [Range(4, 64)]
        public int stepCount = 12;

        [Range(0f, 4f)]
        public float distanceFalloff = 1.0f;

        [Header("Denoise")]
        public bool enableDenoise = true;

        [Range(1, 4)]
        public int denoiseRadius = 2;

        [Range(0.1f, 8f)]
        public float denoiseDepthSigma = 2.0f;

        [Range(1f, 128f)]
        public float denoiseNormalPower = 32.0f;

        [Header("Temporal")]
        public bool enableTemporal = true;

        [Range(0.0f, 0.98f)]
        public float temporalResponse = 0.9f;

        [Range(0.0f, 2.0f)]
        public float temporalClampScale = 2.0f;

        [Range(0.1f, 4.0f)]
        public float indirectClamp = 1.5f;

        [Range(0.0f, 0.05f)]
        public float temporalDepthReject = 0.01f;

        [Range(0.0f, 1.0f)]
        public float temporalNormalReject = 0.85f;

        [Range(0.0f, 1.0f)]
        public float temporalMotionReject = 0.2f;

        [Header("Rendering")]
        [Range(1, 2)]
        public int resolutionDivider = 2;

        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public Settings settings = new Settings();

    private SSGIRenderPass m_Pass;
    private Material m_Material;

    public override void Create()
    {
        Shader shader = Shader.Find("Hidden/Func6/SSGI");
        if (shader == null)
        {
            Debug.LogError("SSGI shader not found: Hidden/Func6/SSGI");
            return;
        }

        if (m_Material != null)
            CoreUtils.Destroy(m_Material);

        m_Material = CoreUtils.CreateEngineMaterial(shader);
        m_Pass = new SSGIRenderPass(m_Material, settings);
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
        m_Pass?.Dispose();
        CoreUtils.Destroy(m_Material);
    }
}
