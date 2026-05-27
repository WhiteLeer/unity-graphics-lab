using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class SSRRenderPass : ScriptableRenderPass
{
    private static bool s_LoggedExecute;
    private readonly Material m_Material;
    private readonly SSRRenderFeature.Settings m_Settings;

    private readonly int m_SourceColor = Shader.PropertyToID("_SSR_SourceColor");
    private readonly int m_ResultRT = Shader.PropertyToID("_SSR_Result");

    private readonly ProfilingSampler m_Sampler = new ProfilingSampler("SSR");

    public SSRRenderPass(Material material, SSRRenderFeature.Settings settings)
    {
        m_Material = material;
        m_Settings = settings;
    }

    public void Setup(ScriptableRenderer renderer)
    {
        ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Color);
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
        desc.msaaSamples = 1;
        desc.depthBufferBits = 0;

        cmd.GetTemporaryRT(m_SourceColor, desc, FilterMode.Bilinear);
        cmd.GetTemporaryRT(m_ResultRT, desc, FilterMode.Bilinear);

        Camera camera = renderingData.cameraData.camera;
        Matrix4x4 view = camera.worldToCameraMatrix;
        Matrix4x4 proj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
        Matrix4x4 invViewProj = (proj * view).inverse;

        m_Material.SetMatrix(Shader.PropertyToID("_SSRView"), view);
        m_Material.SetMatrix(Shader.PropertyToID("_SSRProj"), proj);
        m_Material.SetMatrix(Shader.PropertyToID("_SSRInvViewProj"), invViewProj);

        m_Material.SetVector(Shader.PropertyToID("_SSRParams"), new Vector4(
            m_Settings.intensity,
            m_Settings.maxSteps,
            m_Settings.stride,
            m_Settings.thickness
        ));

        m_Material.SetVector(Shader.PropertyToID("_SSRParams2"), new Vector4(
            m_Settings.maxDistance,
            m_Settings.rayStartBias,
            m_Settings.fresnelPower,
            0.0f
        ));

        m_Material.SetVector(Shader.PropertyToID("_SSRParams3"), new Vector4(
            m_Settings.fadeStart,
            m_Settings.fadeEnd,
            m_Settings.hitSoftness,
            0.0f
        ));

        m_Material.SetVector(Shader.PropertyToID("_SSRScreenSize"), new Vector4(
            desc.width,
            desc.height,
            1.0f / desc.width,
            1.0f / desc.height
        ));

        int debugMode = m_Settings.enableDebugVisualization ? (int)m_Settings.runtimeDebugStep : 0;
        m_Material.SetInt(Shader.PropertyToID("_SSRDebugMode"), debugMode);
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (m_Material == null)
            return;

        if (!s_LoggedExecute)
        {
            s_LoggedExecute = true;
            Debug.Log($"[SSRPass] Execute camera={renderingData.cameraData.camera.name} targetDesc={renderingData.cameraData.cameraTargetDescriptor.width}x{renderingData.cameraData.cameraTargetDescriptor.height}");
        }

        CommandBuffer cmd = CommandBufferPool.Get("SSR");
        using (new ProfilingScope(cmd, m_Sampler))
        {
            RenderTargetIdentifier sourceTarget = renderingData.cameraData.renderer.cameraColorTarget;
            cmd.Blit(sourceTarget, m_SourceColor);
            cmd.SetGlobalTexture(Shader.PropertyToID("_BaseMap"), m_SourceColor);
            cmd.Blit(m_SourceColor, m_ResultRT, m_Material, 0);
            cmd.Blit(m_ResultRT, sourceTarget);
        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    public override void OnCameraCleanup(CommandBuffer cmd)
    {
        cmd.ReleaseTemporaryRT(m_SourceColor);
        cmd.ReleaseTemporaryRT(m_ResultRT);
    }
}
