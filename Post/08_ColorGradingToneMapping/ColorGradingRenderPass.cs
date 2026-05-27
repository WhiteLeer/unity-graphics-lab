using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class ColorGradingRenderPass : ScriptableRenderPass
{
    private readonly Material m_Material;
    private readonly ColorGradingRenderFeature.Settings m_Settings;

    private readonly int m_SourceColor = Shader.PropertyToID("_CG_SourceColor");
    private readonly int m_ResultRT = Shader.PropertyToID("_CG_Result");
    private readonly ProfilingSampler m_Sampler = new ProfilingSampler("Func8 ColorGrading + ToneMapping");

    public ColorGradingRenderPass(Material material, ColorGradingRenderFeature.Settings settings)
    {
        m_Material = material;
        m_Settings = settings;
    }

    public void Setup(ScriptableRenderer renderer)
    {
        ConfigureInput(ScriptableRenderPassInput.Color);
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
        desc.depthBufferBits = 0;
        desc.msaaSamples = 1;

        cmd.GetTemporaryRT(m_SourceColor, desc, FilterMode.Bilinear);
        cmd.GetTemporaryRT(m_ResultRT, desc, FilterMode.Bilinear);

        m_Material.SetVector(Shader.PropertyToID("_CGParams1"), new Vector4(
            m_Settings.exposure,
            m_Settings.contrast,
            m_Settings.saturation,
            m_Settings.hueShift
        ));

        m_Material.SetVector(Shader.PropertyToID("_CGParams2"), new Vector4(
            m_Settings.postExposureEV,
            m_Settings.temperature,
            m_Settings.tint,
            (float)m_Settings.toneMapping
        ));

        m_Material.SetColor(Shader.PropertyToID("_CGColorFilter"), m_Settings.colorFilter);
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (m_Material == null)
            return;

        CommandBuffer cmd = CommandBufferPool.Get("Func8 ColorGrading + ToneMapping");
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
