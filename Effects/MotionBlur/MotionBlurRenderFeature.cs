using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class MotionBlurRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("Motion Blur")]
        [Range(0f, 2f)]
        public float intensity = 1.0f;

        [Range(0f, 180f)]
        public float shutterAngle = 90f;

        [Range(4, 24)]
        public int sampleCount = 8;

        [Range(1f, 64f)]
        public float maxBlurPixels = 24f;

        [Range(0f, 0.25f)]
        public float motionThreshold = 0.002f;

        [Range(0f, 1f)]
        public float centerWeight = 0.2f;

        [Header("Rendering")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public Settings settings = new Settings();

    private MotionBlurRenderPass m_Pass;
    private Material m_Material;

    public override void Create()
    {
        Shader shader = Shader.Find("Hidden/Func7/MotionBlur");
        if (shader == null)
        {
            Debug.LogError("Motion blur shader not found: Hidden/Func7/MotionBlur");
            return;
        }

        if (m_Material != null)
            CoreUtils.Destroy(m_Material);

        m_Material = CoreUtils.CreateEngineMaterial(shader);
        m_Pass = new MotionBlurRenderPass(m_Material, settings);
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
