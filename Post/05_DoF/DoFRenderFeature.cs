using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class DoFRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("Focus")]
        [Range(0.1f, 100f)]
        public float focalDistance = 8.0f;

        [Range(0.01f, 50f)]
        public float focalRange = 2.5f;

        [Header("Blur")]
        [Range(0f, 4f)]
        public float maxBlurRadius = 1.5f;

        [Range(0f, 2f)]
        public float intensity = 1.0f;

        [Range(0f, 2f)]
        public float nearBlurStrength = 1.0f;

        [Range(0f, 2f)]
        public float farBlurStrength = 1.0f;

        [Header("Rendering")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public Settings settings = new Settings();

    private DoFRenderPass m_Pass;
    private Material m_Material;

    public override void Create()
    {
        Shader shader = Shader.Find("Hidden/Func5/DoF");
        if (shader == null)
        {
            Debug.LogError("DoF shader not found: Hidden/Func5/DoF");
            return;
        }

        if (m_Material != null)
            CoreUtils.Destroy(m_Material);

        m_Material = CoreUtils.CreateEngineMaterial(shader);
        m_Pass = new DoFRenderPass(m_Material, settings);
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
