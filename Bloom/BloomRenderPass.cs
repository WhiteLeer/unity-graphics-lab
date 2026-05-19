using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class BloomRenderPass : ScriptableRenderPass
{
    private const int MaxPyramidLevels = 6;

    private readonly Material m_Material;
    private readonly BloomRenderFeature.Settings m_Settings;

    private readonly int m_SourceCopyRT = Shader.PropertyToID("_BloomSourceCopy");
    private readonly int m_ResultRT = Shader.PropertyToID("_BloomResult");
    private readonly int[] m_MipDown = new int[MaxPyramidLevels];
    private readonly int[] m_MipUp = new int[MaxPyramidLevels];

    private static readonly int BloomParamsId = Shader.PropertyToID("_BloomParams");
    private static readonly int BloomColorId = Shader.PropertyToID("_BloomColor");
    private static readonly int BloomLowTexId = Shader.PropertyToID("_BloomLowTex");
    private static readonly int BloomTexId = Shader.PropertyToID("_BloomTex");
    private static readonly int BloomSourceTexelSizeId = Shader.PropertyToID("_BloomSourceTexelSize");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

    private readonly ProfilingSampler m_Sampler = new ProfilingSampler("Func4 Bloom");

    public BloomRenderPass(Material material, BloomRenderFeature.Settings settings)
    {
        m_Material = material;
        m_Settings = settings;

        for (int i = 0; i < MaxPyramidLevels; i++)
        {
            m_MipDown[i] = Shader.PropertyToID($"_BloomMipDown{i}");
            m_MipUp[i] = Shader.PropertyToID($"_BloomMipUp{i}");
        }
    }

    public void Setup(ScriptableRenderer renderer)
    {
        ConfigureInput(ScriptableRenderPassInput.Color);
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (m_Material == null)
            return;

        CommandBuffer cmd = CommandBufferPool.Get("Func4 Bloom");
        using (new ProfilingScope(cmd, m_Sampler))
        {
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;

            int width = Mathf.Max(1, desc.width / 2);
            int height = Mathf.Max(1, desc.height / 2);

            if (width < 2 || height < 2)
            {
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
                return;
            }

            int levels = Mathf.Clamp(m_Settings.iterations, 1, MaxPyramidLevels);

            float threshold = Mathf.Max(0.0f, m_Settings.threshold);
            float softKnee = Mathf.Clamp01(m_Settings.softKnee);
            float knee = Mathf.Max(1e-5f, threshold * softKnee);
            float clampValue = Mathf.Max(threshold, m_Settings.clampValue);

            m_Material.SetVector(BloomParamsId, new Vector4(threshold, knee, clampValue, m_Settings.intensity));
            m_Material.SetColor(BloomColorId, m_Settings.tint);

            RenderTargetIdentifier source = renderingData.cameraData.renderer.cameraColorTarget;
            cmd.GetTemporaryRT(m_SourceCopyRT, desc, FilterMode.Bilinear);
            cmd.Blit(source, m_SourceCopyRT);

            RenderTextureDescriptor mipDesc = desc;
            mipDesc.width = width;
            mipDesc.height = height;
            cmd.GetTemporaryRT(m_MipDown[0], mipDesc, FilterMode.Bilinear);
            cmd.SetGlobalTexture(BaseMapId, m_SourceCopyRT);
            cmd.SetGlobalVector(BloomSourceTexelSizeId, new Vector4(1.0f / mipDesc.width, 1.0f / mipDesc.height, mipDesc.width, mipDesc.height));
            cmd.Blit(m_SourceCopyRT, m_MipDown[0], m_Material, 0);

            int usedLevels = 1;
            for (int i = 1; i < levels; i++)
            {
                width = Mathf.Max(1, width / 2);
                height = Mathf.Max(1, height / 2);
                if (width < 2 || height < 2)
                    break;

                mipDesc.width = width;
                mipDesc.height = height;
                cmd.GetTemporaryRT(m_MipDown[i], mipDesc, FilterMode.Bilinear);
                cmd.SetGlobalTexture(BaseMapId, m_MipDown[i - 1]);
                cmd.SetGlobalVector(BloomSourceTexelSizeId, new Vector4(1.0f / mipDesc.width, 1.0f / mipDesc.height, mipDesc.width, mipDesc.height));
                cmd.Blit(m_MipDown[i - 1], m_MipDown[i], m_Material, 1);
                usedLevels++;
            }

            for (int i = usedLevels - 2; i >= 0; i--)
            {
                mipDesc.width = Mathf.Max(1, desc.width >> (i + 1));
                mipDesc.height = Mathf.Max(1, desc.height >> (i + 1));
                cmd.GetTemporaryRT(m_MipUp[i], mipDesc, FilterMode.Bilinear);
                cmd.SetGlobalTexture(BloomLowTexId, m_MipDown[i + 1]);
                cmd.SetGlobalTexture(BaseMapId, m_MipDown[i]);
                cmd.SetGlobalVector(BloomSourceTexelSizeId, new Vector4(1.0f / mipDesc.width, 1.0f / mipDesc.height, mipDesc.width, mipDesc.height));
                cmd.Blit(m_MipDown[i], m_MipUp[i], m_Material, 2);
                cmd.Blit(m_MipUp[i], m_MipDown[i]);
            }

            cmd.SetGlobalTexture(BloomTexId, m_MipDown[0]);
            cmd.SetGlobalTexture(BaseMapId, m_SourceCopyRT);
            cmd.SetGlobalVector(BloomSourceTexelSizeId, new Vector4(1.0f / desc.width, 1.0f / desc.height, desc.width, desc.height));
            cmd.GetTemporaryRT(m_ResultRT, desc, FilterMode.Bilinear);
            cmd.Blit(m_SourceCopyRT, m_ResultRT, m_Material, 3);
            cmd.Blit(m_ResultRT, source);

            cmd.ReleaseTemporaryRT(m_SourceCopyRT);
            cmd.ReleaseTemporaryRT(m_ResultRT);
            for (int i = 0; i < usedLevels; i++)
            {
                cmd.ReleaseTemporaryRT(m_MipDown[i]);
                if (i < usedLevels - 1)
                    cmd.ReleaseTemporaryRT(m_MipUp[i]);
            }
        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }
}
