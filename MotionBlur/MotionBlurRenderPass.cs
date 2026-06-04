using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class MotionBlurRenderPass : ScriptableRenderPass
{
    private readonly Material m_Material;
    private readonly MotionBlurRenderFeature.Settings m_Settings;

    private readonly int m_SourceColor = Shader.PropertyToID("_MB_SourceColor");
    private readonly int m_ResultRT = Shader.PropertyToID("_MB_Result");
    private readonly ProfilingSampler m_Sampler = new ProfilingSampler("Func7 MotionBlur");

    public MotionBlurRenderPass(Material material, MotionBlurRenderFeature.Settings settings)
    {
        m_Material = material;
        m_Settings = settings;
    }

    public void Setup(ScriptableRenderer renderer)
    {
        ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Motion);
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
        desc.depthBufferBits = 0;
        desc.msaaSamples = 1;

        cmd.GetTemporaryRT(m_SourceColor, desc, FilterMode.Bilinear);
        cmd.GetTemporaryRT(m_ResultRT, desc, FilterMode.Bilinear);

        m_Material.SetVector(Shader.PropertyToID("_MBParams"), new Vector4(
            m_Settings.intensity,
            m_Settings.shutterAngle / 180.0f,
            Mathf.Max(4, m_Settings.sampleCount),
            m_Settings.maxBlurPixels
        ));

        m_Material.SetVector(Shader.PropertyToID("_MBParams2"), new Vector4(
            m_Settings.motionThreshold,
            m_Settings.centerWeight,
            0f,
            0f
        ));

        m_Material.SetVector(Shader.PropertyToID("_MBScreenSize"), new Vector4(
            desc.width,
            desc.height,
            1.0f / desc.width,
            1.0f / desc.height
        ));
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (m_Material == null)
            return;

        CommandBuffer cmd = CommandBufferPool.Get("Func7 MotionBlur");
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
