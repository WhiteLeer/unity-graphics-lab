using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class BloomRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("Bloom")]
        [Range(0f, 5f)]
        public float intensity = 1.0f;

        [Range(0f, 10f)]
        public float threshold = 1.0f;

        [Range(0f, 1f)]
        public float softKnee = 0.5f;

        [Range(0f, 10f)]
        public float clampValue = 10.0f;

        [Range(1, 6)]
        public int iterations = 4;

        [ColorUsage(false, true)]
        public Color tint = Color.white;

        [Header("Rendering")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public Settings settings = new Settings();

    private BloomRenderPass m_Pass;
    private Material m_Material;

    public override void Create()
    {
        Shader shader = Shader.Find("Hidden/Func4/Bloom");
        if (shader == null)
        {
            Debug.LogError("Bloom shader not found: Hidden/Func4/Bloom");
            return;
        }

        if (m_Material != null)
            CoreUtils.Destroy(m_Material);

        m_Material = CoreUtils.CreateEngineMaterial(shader);
        m_Pass = new BloomRenderPass(m_Material, settings);
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
