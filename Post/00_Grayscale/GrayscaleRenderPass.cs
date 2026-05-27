using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class GrayscaleRenderPass : ScriptableRenderPass
{
    private Material material;
    private RenderTargetIdentifier source;
    private RenderTargetHandle tempTexture;
    private float intensity;

    public GrayscaleRenderPass(GrayscaleRenderFeature.Settings settings)
    {
        this.material = settings.material;
        this.intensity = settings.intensity;
        this.renderPassEvent = settings.renderPassEvent;
        tempTexture.Init("_TempGrayscaleTexture");
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        source = renderingData.cameraData.renderer.cameraColorTarget;
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (material == null) return;

        CommandBuffer cmd = CommandBufferPool.Get("Grayscale Effect");

        RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
        desc.depthBufferBits = 0;

        material.SetFloat("_Intensity", intensity);

        cmd.GetTemporaryRT(tempTexture.id, desc);
        cmd.Blit(source, tempTexture.Identifier(), material, 0);
        cmd.Blit(tempTexture.Identifier(), source);
        cmd.ReleaseTemporaryRT(tempTexture.id);

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }
}