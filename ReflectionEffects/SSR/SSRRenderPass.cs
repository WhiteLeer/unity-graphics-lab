using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class SSRRenderPass : ScriptableRenderPass
{
    private static bool s_LoggedExecute;
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int SSRViewId = Shader.PropertyToID("_SSRView");
    private static readonly int SSRProjId = Shader.PropertyToID("_SSRProj");
    private static readonly int SSRInvViewProjId = Shader.PropertyToID("_SSRInvViewProj");
    private static readonly int SSRParamsId = Shader.PropertyToID("_SSRParams");
    private static readonly int SSRScreenSizeId = Shader.PropertyToID("_SSRScreenSize");
    private static readonly int SSRDebugModeId = Shader.PropertyToID("_SSRDebugMode");
    private static readonly int SSRDepthScaleId = Shader.PropertyToID("_SSRDepthScale");
    private static readonly int SSRExtraParamsId = Shader.PropertyToID("_SSRExtraParams");
    private static readonly int SSRExtraParams2Id = Shader.PropertyToID("_SSRExtraParams2");
    private static readonly int SSRReceiverRoughnessMapId = Shader.PropertyToID("_SSRReceiverRoughnessMap");
    private static readonly int SSRReceiverRoughnessSTId = Shader.PropertyToID("_SSRReceiverRoughnessST");
    private static readonly int SSRReceiverRoughnessParamsId = Shader.PropertyToID("_SSRReceiverRoughnessParams");
    private static readonly int SSRAmbientSkyColorId = Shader.PropertyToID("_SSRAmbientSkyColor");
    private static readonly int SSRAmbientEquatorColorId = Shader.PropertyToID("_SSRAmbientEquatorColor");
    private static readonly int SSRAmbientGroundColorId = Shader.PropertyToID("_SSRAmbientGroundColor");
    private readonly Material m_Material;
    private readonly SSRRenderFeature.Settings m_Settings;
    private ScriptableRenderer m_Renderer;

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
        m_Renderer = renderer;
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
        float intensity = Mathf.Clamp(m_Settings.intensity, 0.0f, 2.0f);
        float maxDistance = Mathf.Clamp(m_Settings.maxDistance, 0.1f, 50.0f);
        float step = Mathf.Clamp(m_Settings.step, 0.005f, 0.5f);
        float thickness = Mathf.Clamp(m_Settings.thickness, 0.00001f, 0.2f);
        float receiverNormalThreshold = Mathf.Clamp01(m_Settings.receiverNormalThreshold);
        float receiverNormalFade = Mathf.Clamp01(m_Settings.receiverNormalFade);
        float rayStartBias = Mathf.Clamp01(m_Settings.rayStartBias);
        float reflectionBlend = Mathf.Clamp(m_Settings.reflectionBlend, 0.0f, 2.0f);
        float receiverRoughnessStrength = Mathf.Clamp01(m_Settings.receiverRoughnessStrength);
        float receiverMaxBlurPixels = Mathf.Clamp(m_Settings.receiverMaxBlurPixels, 0.0f, 32.0f);
        float fallbackIntensity = Mathf.Clamp(m_Settings.fallbackIntensity, 0.0f, 2.0f);
        float fallbackRoughness = Mathf.Clamp01(m_Settings.fallbackRoughness);

        m_Material.SetMatrix(SSRViewId, view);
        m_Material.SetMatrix(SSRProjId, proj);
        m_Material.SetMatrix(SSRInvViewProjId, invViewProj);

        m_Material.SetVector(SSRParamsId, new Vector4(
            intensity,
            maxDistance,
            step,
            thickness
        ));

        m_Material.SetVector(SSRScreenSizeId, new Vector4(
            desc.width,
            desc.height,
            1.0f / desc.width,
            1.0f / desc.height
        ));
        m_Material.SetFloat(SSRDepthScaleId, 1.0f / Mathf.Max(camera.farClipPlane, 0.0001f));
        m_Material.SetVector(SSRExtraParamsId, new Vector4(
            receiverNormalThreshold,
            rayStartBias,
            reflectionBlend,
            receiverNormalFade
        ));
        m_Material.SetVector(SSRExtraParams2Id, new Vector4(
            m_Settings.enableReceiverFilter ? 1.0f : 0.0f,
            fallbackIntensity,
            fallbackRoughness,
            0.0f
        ));
        m_Material.SetTexture(SSRReceiverRoughnessMapId, m_Settings.receiverRoughnessMap != null ? m_Settings.receiverRoughnessMap : Texture2D.blackTexture);
        m_Material.SetVector(SSRReceiverRoughnessSTId, new Vector4(
            m_Settings.receiverRoughnessTiling.x,
            m_Settings.receiverRoughnessTiling.y,
            m_Settings.receiverRoughnessOffset.x,
            m_Settings.receiverRoughnessOffset.y
        ));
        m_Material.SetVector(SSRReceiverRoughnessParamsId, new Vector4(
            receiverRoughnessStrength,
            receiverMaxBlurPixels,
            0.0f,
            0.0f
        ));
        m_Material.SetColor(SSRAmbientSkyColorId, RenderSettings.ambientSkyColor);
        m_Material.SetColor(SSRAmbientEquatorColorId, RenderSettings.ambientEquatorColor);
        m_Material.SetColor(SSRAmbientGroundColorId, RenderSettings.ambientGroundColor);

        int debugMode = 0;
        m_Material.SetInt(SSRDebugModeId, debugMode);
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
            Blit(cmd, m_Renderer.cameraColorTarget, m_SourceColor);
            cmd.SetGlobalTexture(BaseMapId, m_SourceColor);
            Blit(cmd, m_SourceColor, m_ResultRT, m_Material, 0);
            Blit(cmd, m_ResultRT, m_Renderer.cameraColorTarget);
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
