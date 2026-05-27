using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// SSAO Render Feature - 完全基于Unity官方实现
/// 算法：Morgan 2011 Alchemy AO
/// </summary>
public class SSAORenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("SSAO Parameters")]
        [Tooltip("AO强度，越大越暗")]
        [Range(0.5f, 4.0f)]
        public float intensity = 3.0f;

        [Tooltip("采样半径，控制AO影响范围")]
        [Range(0.01f, 1.0f)]
        public float radius = 0.035f;

        [Tooltip("采样点数量，越多越平滑")]
        [Range(4, 32)]
        public int sampleCount = 12;

        [Tooltip("自阴影抑制，值越大自阴影越少但整体AO也会减弱")]
        [Range(0.0f, 0.1f)]
        public float beta = 0.002f;

        [Tooltip("启用双边模糊")]
        public bool enableBlur = true;

        [Header("Debug Visualization")]
        public bool enableDebugVisualization = false;

        public enum SSAODebugStep
        {
            None = 0,
            Depth = 1,
            ViewPosVector = 5,
            WorldPosition = 16,
            Normals = 10,
            RawAO = 6,
            FinalAO = 7
        }

        public SSAODebugStep debugStep1 = SSAODebugStep.WorldPosition;
        public SSAODebugStep debugStep2 = SSAODebugStep.ViewPosVector;
        public SSAODebugStep debugStep3 = SSAODebugStep.Depth;
        public SSAODebugStep debugStep4 = SSAODebugStep.FinalAO;
        public SSAODebugStep runtimeDebugStep = SSAODebugStep.FinalAO;

        [Header("Rendering")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    public Settings settings = new Settings();
    private SSAORenderPass m_Pass;
    private Material m_Material;

    public override void Create()
    {
        Shader shader = Shader.Find("Hidden/SSAO");
        if (shader == null)
        {
            Debug.LogError("SSAO shader not found!");
            return;
        }

        if (m_Material != null)
            CoreUtils.Destroy(m_Material);
        m_Material = CoreUtils.CreateEngineMaterial(shader);

        m_Pass = new SSAORenderPass(m_Material, settings);
        m_Pass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (m_Material == null || m_Pass == null)
            return;

        if (renderingData.cameraData.cameraType != CameraType.Game &&
            renderingData.cameraData.cameraType != CameraType.SceneView)
            return;

        m_Pass.Setup(renderer);
        renderer.EnqueuePass(m_Pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(m_Material);
    }
}
