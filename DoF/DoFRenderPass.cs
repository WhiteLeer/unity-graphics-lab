using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class DoFRenderPass : ScriptableRenderPass
{
    private readonly Material m_Material;
    private readonly DoFRenderFeature.Settings m_Settings;

    private readonly int m_SourceCopyRT = Shader.PropertyToID("_DoFSourceCopy");
    private readonly int m_HalfRT = Shader.PropertyToID("_DoFHalf");
    private readonly int m_BlurART = Shader.PropertyToID("_DoFBlurA");
    private readonly int m_BlurBRT = Shader.PropertyToID("_DoFBlurB");
    private readonly int m_ResultRT = Shader.PropertyToID("_DoFResult");

    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int DoFBlurTexId = Shader.PropertyToID("_DoFBlurTex");
    private static readonly int DoFParams1Id = Shader.PropertyToID("_DoFParams1");
    private static readonly int DoFParams2Id = Shader.PropertyToID("_DoFParams2");
    private static readonly int DoFTexelSizeId = Shader.PropertyToID("_DoFTexelSize");

    private readonly ProfilingSampler m_Sampler = new ProfilingSampler("Func5 DoF");

    public DoFRenderPass(Material material, DoFRenderFeature.Settings settings)
    {
        m_Material = material;
        m_Settings = settings;
    }

    public void Setup(ScriptableRenderer renderer)
    {
        ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (m_Material == null)
            return;

        CommandBuffer cmd = CommandBufferPool.Get("Func5 DoF");
        using (new ProfilingScope(cmd, m_Sampler))
        {
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;

            int halfW = Mathf.Max(1, desc.width / 2);
            int halfH = Mathf.Max(1, desc.height / 2);
            if (halfW < 2 || halfH < 2)
            {
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
                return;
            }

            RenderTargetIdentifier source = renderingData.cameraData.renderer.cameraColorTarget;

            cmd.GetTemporaryRT(m_SourceCopyRT, desc, FilterMode.Bilinear);
            cmd.GetTemporaryRT(m_ResultRT, desc, FilterMode.Bilinear);

            RenderTextureDescriptor halfDesc = desc;
            halfDesc.width = halfW;
            halfDesc.height = halfH;
            cmd.GetTemporaryRT(m_HalfRT, halfDesc, FilterMode.Bilinear);
            cmd.GetTemporaryRT(m_BlurART, halfDesc, FilterMode.Bilinear);
            cmd.GetTemporaryRT(m_BlurBRT, halfDesc, FilterMode.Bilinear);

            m_Material.SetVector(DoFParams1Id, new Vector4(
                m_Settings.focalDistance,
                m_Settings.focalRange,
                m_Settings.maxBlurRadius,
                m_Settings.intensity
            ));
            m_Material.SetVector(DoFParams2Id, new Vector4(
                m_Settings.nearBlurStrength,
                m_Settings.farBlurStrength,
                0f,
                0f
            ));

            cmd.Blit(source, m_SourceCopyRT);

            cmd.SetGlobalTexture(BaseMapId, m_SourceCopyRT);
            cmd.SetGlobalVector(DoFTexelSizeId, new Vector4(1.0f / halfW, 1.0f / halfH, halfW, halfH));
            cmd.Blit(m_SourceCopyRT, m_HalfRT, m_Material, 0);

            cmd.SetGlobalTexture(BaseMapId, m_HalfRT);
            cmd.Blit(m_HalfRT, m_BlurART, m_Material, 1);

            cmd.SetGlobalTexture(BaseMapId, m_BlurART);
            cmd.Blit(m_BlurART, m_BlurBRT, m_Material, 2);

            cmd.SetGlobalTexture(BaseMapId, m_SourceCopyRT);
            cmd.SetGlobalTexture(DoFBlurTexId, m_BlurBRT);
            cmd.Blit(m_SourceCopyRT, m_ResultRT, m_Material, 3);

            cmd.Blit(m_ResultRT, source);

            cmd.ReleaseTemporaryRT(m_SourceCopyRT);
            cmd.ReleaseTemporaryRT(m_HalfRT);
            cmd.ReleaseTemporaryRT(m_BlurART);
            cmd.ReleaseTemporaryRT(m_BlurBRT);
            cmd.ReleaseTemporaryRT(m_ResultRT);
        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }
}
