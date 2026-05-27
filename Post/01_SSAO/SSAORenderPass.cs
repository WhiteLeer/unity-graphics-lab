using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class SSAORenderPass : ScriptableRenderPass
{
    private static readonly int s_DebugModeId = Shader.PropertyToID("_DebugMode");
    private Material m_Material;
    private SSAORenderFeature.Settings m_Settings;
    private ScriptableRenderer m_Renderer;

    // 4个RT（与Unity一致）
    private int m_SSAORT1 = Shader.PropertyToID("_SSAO_RT1");
    private int m_SSAORT2 = Shader.PropertyToID("_SSAO_RT2");
    private int m_SSAORT3 = Shader.PropertyToID("_SSAO_RT3");
    private int m_SSAOFinal = Shader.PropertyToID("_SSAO_Texture");

    private ProfilingSampler m_Sampler = new ProfilingSampler("SSAO");

    public SSAORenderPass(Material material, SSAORenderFeature.Settings settings)
    {
        m_Material = material;
        m_Settings = settings;
    }

    public void Setup(ScriptableRenderer renderer)
    {
        m_Renderer = renderer;
        ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
        desc.msaaSamples = 1;
        desc.depthBufferBits = 0;

        // RT1: AO计算（ARGB32存AO+Normal）
        desc.colorFormat = RenderTextureFormat.ARGB32;
        cmd.GetTemporaryRT(m_SSAORT1, desc, FilterMode.Bilinear);

        // RT2: 水平模糊
        cmd.GetTemporaryRT(m_SSAORT2, desc, FilterMode.Bilinear);

        // RT3: 垂直模糊
        cmd.GetTemporaryRT(m_SSAORT3, desc, FilterMode.Bilinear);

        // Final: 最终AO（R8）
        desc.colorFormat = RenderTextureFormat.R8;
        cmd.GetTemporaryRT(m_SSAOFinal, desc, FilterMode.Bilinear);

        // 设置参数
        Camera camera = renderingData.cameraData.camera;

        // SSAO参数
        m_Material.SetVector(Shader.PropertyToID("_SSAOParams"), new Vector4(
            m_Settings.intensity,
            m_Settings.radius,
            m_Settings.beta,
            m_Settings.sampleCount
        ));

        // 纹理尺寸
        m_Material.SetVector(Shader.PropertyToID("_SourceSize"), new Vector4(
            desc.width,
            desc.height,
            1.0f / desc.width,
            1.0f / desc.height
        ));

        int debugStep = m_Settings.enableDebugVisualization ? (int)m_Settings.runtimeDebugStep : -1;
        m_Material.SetInt(s_DebugModeId, debugStep);

        // View空间重建参数（Unity方法）
        SetupViewSpaceParams(camera);
    }

    private void SetupViewSpaceParams(Camera camera)
    {
        Matrix4x4 view = camera.worldToCameraMatrix;
        Matrix4x4 proj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
        Matrix4x4 cview = view;
        cview.SetColumn(3, new Vector4(0, 0, 0, 1));
        Matrix4x4 cviewProj = proj * cview;

        // 1) 在View空间计算近平面角点
        float tanHalfFov = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float aspect = camera.aspect;
        Vector3 topLeftView = new Vector3(-tanHalfFov * aspect, tanHalfFov, -1f);
        Vector3 topRightView = new Vector3(tanHalfFov * aspect, tanHalfFov, -1f);
        Vector3 bottomLeftView = new Vector3(-tanHalfFov * aspect, -tanHalfFov, -1f);

        // 2) 旋转到世界空间向量（不带平移）
        Matrix4x4 cviewInv = cview.inverse;
        Vector3 topLeft = cviewInv.MultiplyPoint3x4(topLeftView);
        Vector3 topRight = cviewInv.MultiplyPoint3x4(topRightView);
        Vector3 bottomLeft = cviewInv.MultiplyPoint3x4(bottomLeftView);

        Vector4 xExtent = topRight - topLeft;
        Vector4 yExtent = bottomLeft - topLeft;

        m_Material.SetVector(Shader.PropertyToID("_CameraViewTopLeftCorner"), topLeft);
        m_Material.SetVector(Shader.PropertyToID("_CameraViewXExtent"), xExtent);
        m_Material.SetVector(Shader.PropertyToID("_CameraViewYExtent"), yExtent);
        m_Material.SetVector(Shader.PropertyToID("_ProjectionParams2"), new Vector4(1.0f / camera.nearClipPlane, 0, 0, 0));

        // 这里必须是 cviewProj（去平移），与世界空间向量重建保持一致。
        m_Material.SetMatrix(Shader.PropertyToID("_CameraViewProjections"), cviewProj);

        // View matrix Z row for z distance reconstruction (same intent as URP's UNITY_MATRIX_V[2].xyz).
        Vector4 viewZRow = new Vector4(view.m20, view.m21, view.m22, view.m23);
        m_Material.SetVector(Shader.PropertyToID("_CameraViewZRow"), viewZRow);
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (m_Material == null)
            return;

        CommandBuffer cmd = CommandBufferPool.Get("SSAO");

        using (new ProfilingScope(cmd, m_Sampler))
        {
            // Pass 0: 计算AO（输出到RT1）
            Blit(cmd, m_Renderer.cameraColorTarget, m_SSAORT1, m_Material, 0);

            if (m_Settings.enableDebugVisualization)
            {
                // Debug模式直接显示Pass0输出，避免被后续pack/blur/final逻辑污染。
                Blit(cmd, m_SSAORT1, m_Renderer.cameraColorTarget);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
                return;
            }

            if (m_Settings.enableBlur)
            {
                // Pass 1: 水平模糊（RT1 → RT2）
                cmd.SetGlobalTexture(Shader.PropertyToID("_BaseMap"), m_SSAORT1);
                Blit(cmd, m_SSAORT1, m_SSAORT2, m_Material, 1);

                // Pass 2: 垂直模糊（RT2 → RT3）
                cmd.SetGlobalTexture(Shader.PropertyToID("_BaseMap"), m_SSAORT2);
                Blit(cmd, m_SSAORT2, m_SSAORT3, m_Material, 2);

                // Pass 3: 最终输出（RT3 → Final）
                cmd.SetGlobalTexture(Shader.PropertyToID("_BaseMap"), m_SSAORT3);
                Blit(cmd, m_SSAORT3, m_SSAOFinal, m_Material, 3);
            }
            else
            {
                // 不模糊：直接使用RT1
                cmd.SetGlobalTexture(Shader.PropertyToID("_BaseMap"), m_SSAORT1);
                Blit(cmd, m_SSAORT1, m_SSAOFinal, m_Material, 3);
            }

            // 设置全局AO纹理
            cmd.SetGlobalTexture("_ScreenSpaceOcclusionTexture", m_SSAOFinal);

            // Pass 4: 应用到场景（乘法混合）
            Blit(cmd, m_Renderer.cameraColorTarget, m_Renderer.cameraColorTarget, m_Material, 4);
        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    public override void OnCameraCleanup(CommandBuffer cmd)
    {
        cmd.ReleaseTemporaryRT(m_SSAORT1);
        cmd.ReleaseTemporaryRT(m_SSAORT2);
        cmd.ReleaseTemporaryRT(m_SSAORT3);
        cmd.ReleaseTemporaryRT(m_SSAOFinal);
    }
}
