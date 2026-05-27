using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// SSR Render Feature - Screen Space Reflection
/// 通用屏幕空间反射（适用于非平面场景，成本高于SSPR）
/// </summary>
public class SSRRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("SSR Parameters")]
        [Range(0.0f, 2.0f)]
        public float intensity = 1.15f;

        [Range(8, 512)]
        public int maxSteps = 64;

        [Range(0.05f, 2.0f)]
        public float stride = 0.2f;

        [Range(0.001f, 0.2f)]
        public float thickness = 0.06f;

        [Range(0.05f, 50.0f)]
        public float maxDistance = 12.0f;

        [Range(0.001f, 0.1f)]
        public float rayStartBias = 0.03f;

        [Range(0.0f, 8.0f)]
        public float fresnelPower = 4.0f;

        [Header("Edge Fade")]
        [Range(0.0f, 0.5f)]
        public float fadeStart = 0.0f;

        [Range(0.0f, 0.5f)]
        public float fadeEnd = 0.08f;

        [Range(0.0f, 1.0f)]
        public float hitSoftness = 0.35f;

        [Header("Debug Visualization")]
        public bool enableDebugVisualization = false;

        public enum SSRDebugStep
        {
            None = 0,
            Depth = 1,
            ViewPosVector = 5,
            FinalResult = 7,
            Normals = 10,
            WorldPosition = 16,
            ReflectionUV = 17,
            HitMask = 18
        }

        public SSRDebugStep debugStep1 = SSRDebugStep.WorldPosition;
        public SSRDebugStep debugStep2 = SSRDebugStep.Normals;
        public SSRDebugStep debugStep3 = SSRDebugStep.ReflectionUV;
        public SSRDebugStep debugStep4 = SSRDebugStep.HitMask;
        public SSRDebugStep runtimeDebugStep = SSRDebugStep.None;

        [Header("Rendering")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
    }

    public Settings settings = new Settings();

    private SSRRenderPass m_Pass;
    private Material m_Material;

    public override void Create()
    {
        Shader shader = Shader.Find("Hidden/SSR");
        if (shader == null)
        {
            Debug.LogError("SSR shader not found!");
            return;
        }

        if (m_Material != null)
            CoreUtils.Destroy(m_Material);
        m_Material = CoreUtils.CreateEngineMaterial(shader);

        m_Pass = new SSRRenderPass(m_Material, settings);
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
