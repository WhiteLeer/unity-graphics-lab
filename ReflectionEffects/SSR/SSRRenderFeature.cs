using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

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
        public float intensity = 1.0f;

        [Range(0.1f, 50.0f)]
        public float maxDistance = 15.0f;

        [Range(0.005f, 0.5f)]
        [FormerlySerializedAs("stride")]
        public float step = 0.05f;

        [Range(0.00001f, 0.2f)]
        public float thickness = 0.0006f;

        [Range(0.0f, 1.0f)]
        public float receiverNormalThreshold = 0.9f;

        public bool enableReceiverFilter = true;

        [Range(0.0f, 1.0f)]
        public float receiverNormalFade = 0.12f;

        [Range(0.0f, 1.0f)]
        public float rayStartBias = 0.1f;

        [Range(0.0f, 2.0f)]
        public float reflectionBlend = 1.0f;

        [Header("Receiver Roughness")]
        public Texture2D receiverRoughnessMap;

        public Vector2 receiverRoughnessTiling = new Vector2(0.25f, 0.25f);

        public Vector2 receiverRoughnessOffset = Vector2.zero;

        [Range(0.0f, 1.0f)]
        public float receiverRoughnessStrength = 1.0f;

        [Range(0.0f, 64.0f)]
        public float receiverMaxBlurPixels = 24.0f;

        [Range(0.0f, 2.0f)]
        public float fallbackIntensity = 0.35f;

        [Range(0.0f, 1.0f)]
        public float fallbackRoughness = 0.08f;

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
        Shader shader = Shader.Find("Hidden/SSR_ReflectionProbe");
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

        if (renderingData.cameraData.cameraType != CameraType.Game)
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
