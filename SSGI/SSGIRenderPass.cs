using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

public class SSGIRenderPass : ScriptableRenderPass
{
    private class CameraHistory
    {
        public RenderTexture historyRT;
        public Matrix4x4 prevViewProj;
        public bool valid;
        public int width;
        public int height;
        public int frameIndex;
    }

    private readonly Material m_Material;
    private readonly SSGIRenderFeature.Settings m_Settings;
    private readonly Dictionary<int, CameraHistory> m_CameraHistories = new Dictionary<int, CameraHistory>();

    private readonly int m_SourceColor = Shader.PropertyToID("_SSGI_SourceColor");
    private readonly int m_RawRT = Shader.PropertyToID("_SSGI_Raw");
    private readonly int m_DenoiseRT = Shader.PropertyToID("_SSGI_Denoise");
    private readonly int m_Denoise2RT = Shader.PropertyToID("_SSGI_Denoise2");
    private readonly int m_AccumRT = Shader.PropertyToID("_SSGI_Accum");
    private readonly int m_ResultRT = Shader.PropertyToID("_SSGI_Result");
    private readonly ProfilingSampler m_Sampler = new ProfilingSampler("SSGI");

    public SSGIRenderPass(Material material, SSGIRenderFeature.Settings settings)
    {
        m_Material = material;
        m_Settings = settings;
    }

    public void Setup(ScriptableRenderer renderer)
    {
        ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Motion);
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        RenderTextureDescriptor fullDesc = renderingData.cameraData.cameraTargetDescriptor;
        fullDesc.msaaSamples = 1;
        fullDesc.depthBufferBits = 0;

        int divider = Mathf.Clamp(m_Settings.resolutionDivider, 1, 2);
        RenderTextureDescriptor giDesc = fullDesc;
        giDesc.width = Mathf.Max(1, fullDesc.width / divider);
        giDesc.height = Mathf.Max(1, fullDesc.height / divider);

        cmd.GetTemporaryRT(m_SourceColor, fullDesc, FilterMode.Bilinear);
        cmd.GetTemporaryRT(m_RawRT, giDesc, FilterMode.Bilinear);
        cmd.GetTemporaryRT(m_DenoiseRT, giDesc, FilterMode.Bilinear);
        cmd.GetTemporaryRT(m_Denoise2RT, giDesc, FilterMode.Bilinear);
        cmd.GetTemporaryRT(m_AccumRT, giDesc, FilterMode.Bilinear);
        cmd.GetTemporaryRT(m_ResultRT, fullDesc, FilterMode.Bilinear);

        Camera camera = renderingData.cameraData.camera;
        Matrix4x4 view = camera.worldToCameraMatrix;
        Matrix4x4 proj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
        Matrix4x4 viewProj = proj * view;
        Matrix4x4 invViewProj = viewProj.inverse;

        m_Material.SetMatrix(Shader.PropertyToID("_SSGIViewProj"), viewProj);
        m_Material.SetMatrix(Shader.PropertyToID("_SSGIInvViewProj"), invViewProj);
        m_Material.SetVector(Shader.PropertyToID("_SSGICameraPos"), camera.transform.position);

        m_Material.SetVector(Shader.PropertyToID("_SSGIParams"), new Vector4(
            m_Settings.intensity,
            m_Settings.rayLength,
            m_Settings.thickness,
            m_Settings.rayBias
        ));

        m_Material.SetVector(Shader.PropertyToID("_SSGIParams2"), new Vector4(
            Mathf.Max(1, m_Settings.sampleCount),
            Mathf.Max(1, m_Settings.stepCount),
            m_Settings.distanceFalloff,
            0.0f
        ));

        m_Material.SetVector(Shader.PropertyToID("_SSGIParamsDenoise"), new Vector4(
            m_Settings.enableDenoise ? 1.0f : 0.0f,
            Mathf.Max(1, m_Settings.denoiseRadius),
            Mathf.Max(0.1f, m_Settings.denoiseDepthSigma),
            Mathf.Max(1.0f, m_Settings.denoiseNormalPower)
        ));

        m_Material.SetVector(Shader.PropertyToID("_SSGIParamsTemporal"), new Vector4(
            m_Settings.enableTemporal ? 1.0f : 0.0f,
            m_Settings.temporalResponse,
            m_Settings.temporalClampScale,
            m_Settings.indirectClamp
        ));
        m_Material.SetVector(Shader.PropertyToID("_SSGIParamsTemporalReject"), new Vector4(
            m_Settings.temporalDepthReject,
            m_Settings.temporalNormalReject,
            m_Settings.temporalMotionReject,
            0.0f
        ));

        m_Material.SetVector(Shader.PropertyToID("_SSGIScreenSize"), new Vector4(
            giDesc.width,
            giDesc.height,
            1.0f / giDesc.width,
            1.0f / giDesc.height
        ));

        m_Material.SetVector(Shader.PropertyToID("_SSGIFullScreenSize"), new Vector4(
            fullDesc.width,
            fullDesc.height,
            1.0f / fullDesc.width,
            1.0f / fullDesc.height
        ));
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (m_Material == null)
            return;

        CommandBuffer cmd = CommandBufferPool.Get("SSGI");
        using (new ProfilingScope(cmd, m_Sampler))
        {
            Camera camera = renderingData.cameraData.camera;
            int cameraId = camera.GetInstanceID();
            int divider = Mathf.Clamp(m_Settings.resolutionDivider, 1, 2);
            int historyW = Mathf.Max(1, renderingData.cameraData.cameraTargetDescriptor.width / divider);
            int historyH = Mathf.Max(1, renderingData.cameraData.cameraTargetDescriptor.height / divider);
            CameraHistory history = GetOrCreateHistory(cameraId, historyW, historyH);

            Matrix4x4 view = camera.worldToCameraMatrix;
            Matrix4x4 proj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            Matrix4x4 viewProj = proj * view;

            RenderTargetIdentifier sourceTarget = renderingData.cameraData.renderer.cameraColorTarget;
            cmd.Blit(sourceTarget, m_SourceColor);

            // Pass 0: raw indirect GI
            cmd.SetGlobalTexture(Shader.PropertyToID("_SSGISourceTex"), m_SourceColor);
            cmd.SetGlobalTexture(Shader.PropertyToID("_BaseMap"), m_SourceColor);
            cmd.Blit(m_SourceColor, m_RawRT, m_Material, 0);

            // Pass 1: spatial denoise (bilateral)
            if (m_Settings.enableDenoise)
            {
                m_Material.SetVector(Shader.PropertyToID("_SSGIDenoiseDir"), new Vector4(1f, 0f, 0f, 0f));
                cmd.SetGlobalTexture(Shader.PropertyToID("_BaseMap"), m_RawRT);
                cmd.Blit(m_RawRT, m_DenoiseRT, m_Material, 1);

                m_Material.SetVector(Shader.PropertyToID("_SSGIDenoiseDir"), new Vector4(0f, 1f, 0f, 0f));
                cmd.SetGlobalTexture(Shader.PropertyToID("_BaseMap"), m_DenoiseRT);
                cmd.Blit(m_DenoiseRT, m_Denoise2RT, m_Material, 1);
            }
            else
            {
                cmd.Blit(m_RawRT, m_Denoise2RT);
            }

            // Pass 2: temporal accumulation (reproject to previous frame)
            m_Material.SetMatrix(Shader.PropertyToID("_SSGIPrevViewProj"), history.prevViewProj);
            m_Material.SetInt(Shader.PropertyToID("_SSGIHistoryValid"), (m_Settings.enableTemporal && history.valid) ? 1 : 0);
            m_Material.SetFloat(Shader.PropertyToID("_SSGIFrameIndex"), history.frameIndex);
            cmd.SetGlobalTexture(Shader.PropertyToID("_SSGIHistoryTex"), history.historyRT);
            cmd.SetGlobalTexture(Shader.PropertyToID("_BaseMap"), m_Denoise2RT);
            cmd.Blit(m_Denoise2RT, m_AccumRT, m_Material, 2);

            // Save indirect history for next frame.
            cmd.Blit(m_AccumRT, history.historyRT);

            // Pass 3: composite to scene color
            cmd.SetGlobalTexture(Shader.PropertyToID("_SSGISourceTex"), m_SourceColor);
            cmd.SetGlobalTexture(Shader.PropertyToID("_SSGIIndirectTex"), m_AccumRT);
            cmd.SetGlobalTexture(Shader.PropertyToID("_BaseMap"), m_SourceColor);
            cmd.Blit(m_SourceColor, m_ResultRT, m_Material, 3);
            cmd.Blit(m_ResultRT, sourceTarget);

            history.prevViewProj = viewProj;
            history.valid = true;
            history.frameIndex = (history.frameIndex + 1) & 1023;
        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    public override void OnCameraCleanup(CommandBuffer cmd)
    {
        cmd.ReleaseTemporaryRT(m_SourceColor);
        cmd.ReleaseTemporaryRT(m_RawRT);
        cmd.ReleaseTemporaryRT(m_DenoiseRT);
        cmd.ReleaseTemporaryRT(m_Denoise2RT);
        cmd.ReleaseTemporaryRT(m_AccumRT);
        cmd.ReleaseTemporaryRT(m_ResultRT);
    }

    public void Dispose()
    {
        foreach (var kv in m_CameraHistories)
        {
            if (kv.Value.historyRT != null)
            {
                kv.Value.historyRT.Release();
                Object.DestroyImmediate(kv.Value.historyRT);
            }
        }
        m_CameraHistories.Clear();
    }

    private CameraHistory GetOrCreateHistory(int cameraId, int width, int height)
    {
        if (!m_CameraHistories.TryGetValue(cameraId, out CameraHistory history))
        {
            history = new CameraHistory();
            m_CameraHistories[cameraId] = history;
        }

        if (history.historyRT == null || history.width != width || history.height != height)
        {
            if (history.historyRT != null)
            {
                history.historyRT.Release();
                Object.DestroyImmediate(history.historyRT);
            }

            RenderTexture rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf);
            rt.name = $"SSGI_History_{cameraId}";
            rt.filterMode = FilterMode.Bilinear;
            rt.wrapMode = TextureWrapMode.Clamp;
            rt.useMipMap = false;
            rt.autoGenerateMips = false;
            rt.Create();

            history.historyRT = rt;
            history.width = width;
            history.height = height;
            history.valid = false;
            history.prevViewProj = Matrix4x4.identity;
            history.frameIndex = 0;
        }

        return history;
    }
}
