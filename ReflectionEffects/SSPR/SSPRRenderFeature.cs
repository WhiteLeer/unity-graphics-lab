using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// SSPR Render Feature - 屏幕空间平面反射
/// 专门用于平面反射（水面、镜子、地板）
/// </summary>
public class SSPRRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("Reflection Plane")]
        [Tooltip("反射平面的法线（世界空间）")]
        public Vector3 planeNormal = Vector3.up;

        [Tooltip("反射平面到原点的距离（世界空间）")]
        public float planeDistance = 0.0f;

        [Header("Reflection Parameters")]
        [Tooltip("反射强度")]
        [Range(0.0f, 1.0f)]
        public float intensity = 0.8f;

        [Tooltip("菲涅尔效应强度（值越大，掠射角反射越强）")]
        [Range(1.0f, 10.0f)]
        public float fresnelPower = 5.0f;

        [Header("Surface Filter")]
        [Tooltip("表面法线与平面法线的最小点乘（越大越严格）")]
        [Range(0.0f, 1.0f)]
        public float normalThreshold = 0.5f;

        [Tooltip("到平面的最大距离（0=禁用距离过滤）")]
        [Range(0.0f, 1.0f)]
        public float maxPlaneDistance = 0.0f;

        [Header("Reflector Bounds")]
        [Tooltip("是否启用反射区域矩形约束（PPT推荐）")]
        public bool enableReflectorBounds = true;

        [Tooltip("反射区域中心（世界空间）")]
        public Vector3 reflectorCenter = Vector3.zero;

        [Tooltip("反射区域尺寸（X=宽, Y=高）")]
        public Vector2 reflectorSize = new Vector2(10.0f, 10.0f);

        [Header("Visibility Test")]
        [Tooltip("投射可见性厚度阈值（越大越宽松）")]
        [Range(0.0f, 0.5f)]
        public float occlusionThickness = 0.05f;

        [Header("Depth Consistency")]
        [Tooltip("反射深度一致性阈值（越大越宽松）")]
        [Range(0.001f, 0.1f)]
        public float depthConsistency = 0.03f;

        [Header("Hole Fill (SSPR)")]
        [Tooltip("是否启用空洞填充")]
        public bool enableHoleFill = true;

        [Tooltip("空洞填充搜索半径（像素）")]
        [Range(1, 4)]
        public int holeFillRadius = 2;

        [Header("Roughness")]
        [Tooltip("反射粗糙度（0=镜面，1=模糊）")]
        [Range(0.0f, 1.0f)]
        public float roughness = 0.2f;

        [Tooltip("最大模糊像素半径")]
        [Range(0.0f, 4.0f)]
        public float maxBlurPixels = 2.0f;

        [Header("Edge Fade")]
        [Tooltip("边界淡出起始距离")]
        [Range(0.0f, 0.5f)]
        public float fadeStart = 0.0f;

        [Tooltip("边界淡出结束距离")]
        [Range(0.0f, 0.5f)]
        public float fadeEnd = 0.1f;

        [Header("Debug Visualization")]
        public bool enableDebugVisualization = false;

        [Tooltip("调试：跳过法线阈值过滤")]
        public bool debugBypassNormalFilter = false;

        [Tooltip("调试：跳过深度一致性过滤")]
        public bool debugBypassDepthConsistency = false;

        [Tooltip("调试：跳过Compute可见性遮挡过滤")]
        public bool debugBypassComputeOcclusion = false;

        public enum SSPRDebugStep
        {
            None = 0,
            Depth = 1,
            DistanceToPlane = 2,
            ViewPosVector = 5,
            FinalResult = 7,
            Normals = 10,
            NormalDot = 11,
            WorldPosition = 16,
            ReflectedPosition = 17,
            ClipSpace = 18,
            SSPR_OffsetValid = 19,
            SSPR_OffsetUV = 20,
            SSPR_SampledColor = 21,
            SSPR_OffsetCoverage = 22
        }

        public SSPRDebugStep debugStep1 = SSPRDebugStep.SSPR_OffsetValid;
        public SSPRDebugStep debugStep2 = SSPRDebugStep.SSPR_OffsetCoverage;
        public SSPRDebugStep debugStep3 = SSPRDebugStep.SSPR_SampledColor;
        public SSPRDebugStep debugStep4 = SSPRDebugStep.ReflectedPosition;

        [Header("Rendering")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;

        [Header("SSPR Compute")]
        [Tooltip("SSPR compute shader (screen-space planar reflections)")]
        public ComputeShader ssprCompute;
    }

    public Settings settings = new Settings();
    private SSPRRenderPass m_Pass;
    private Material m_Material;

    public override void Create()
    {
        EnsureResources();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        EnsureResources();
        if (m_Material == null || m_Pass == null)
        {
            Debug.LogWarning("[SSPR Feature] Material or Pass is null!");
            return;
        }

        if (renderingData.cameraData.cameraType != CameraType.Game &&
            renderingData.cameraData.cameraType != CameraType.SceneView)
        {
            return;
        }

        m_Pass.Setup(renderer);
        renderer.EnqueuePass(m_Pass);
    }

    private void EnsureResources()
    {
        Shader shader = Shader.Find("Hidden/SSPR");
        if (shader == null)
        {
            Debug.LogError("SSPR shader not found!");
            return;
        }

        if (m_Material == null)
        {
            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        // 归一化平面法线
        settings.planeNormal.Normalize();

        if (m_Pass == null || m_Pass.Material != m_Material)
        {
            m_Pass = new SSPRRenderPass(m_Material, settings, settings.ssprCompute);
            m_Pass.renderPassEvent = settings.renderPassEvent;
        }
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(m_Material);
        m_Material = null;
        m_Pass = null;
    }
}
