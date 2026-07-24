using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed class VolumeThicknessRendererFeature : ScriptableRendererFeature
{
    [Serializable]
    public sealed class Settings
    {
        [Range(0.25f, 1.0f)]
        public float resolutionScale = 1.0f;

        public LayerMask layerMask = ~0;
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
    }

    [SerializeField] private Settings settings = new Settings();

    private VolumeThicknessBackfacePass pass;

    public override void Create()
    {
        pass?.Dispose();
        pass = new VolumeThicknessBackfacePass(settings)
        {
            renderPassEvent = settings.renderPassEvent
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var cameraType = renderingData.cameraData.cameraType;
        if (pass == null ||
            (cameraType != CameraType.Game && cameraType != CameraType.SceneView))
        {
            return;
        }

        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        pass?.Dispose();
        pass = null;
    }

    private sealed class VolumeThicknessBackfacePass : ScriptableRenderPass
    {
        private static readonly int VolumeBackfaceDepthTextureId = Shader.PropertyToID("_VolumeBackfaceDepthTexture");
        private static readonly int VolumeThicknessAvailableId = Shader.PropertyToID("_VolumeThicknessAvailable");
        private static readonly int JadeBackfaceDepthTextureId = Shader.PropertyToID("_JadeBackfaceDepthTexture");
        private static readonly int JadeThicknessAvailableId = Shader.PropertyToID("_JadeThicknessAvailable");
        private static readonly int CrystalBackfaceDepthTextureId = Shader.PropertyToID("_CrystalBackfaceDepthTexture");
        private static readonly int CrystalThicknessAvailableId = Shader.PropertyToID("_CrystalThicknessAvailable");

        private static readonly List<ShaderTagId> ThicknessPassTags = new List<ShaderTagId>
        {
            new ShaderTagId("JadeThicknessBackface"),
            new ShaderTagId("CrystalThicknessBackface"),
            new ShaderTagId("WaterThicknessBackface")
        };

        private readonly Settings settings;
        private readonly ProfilingSampler profilingSampler = new ProfilingSampler("Volume Mesh Thickness");
        private FilteringSettings filteringSettings;
        private RTHandle backfaceDepthTexture;

        public VolumeThicknessBackfacePass(Settings settings)
        {
            this.settings = settings;
            filteringSettings = new FilteringSettings(RenderQueueRange.all, settings.layerMask);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            var scale = Mathf.Clamp(settings.resolutionScale, 0.25f, 1.0f);
            descriptor.width = Mathf.Max(1, Mathf.RoundToInt(descriptor.width * scale));
            descriptor.height = Mathf.Max(1, Mathf.RoundToInt(descriptor.height * scale));
            descriptor.msaaSamples = 1;
            descriptor.depthBufferBits = 32;
            descriptor.graphicsFormat = GraphicsFormat.R32_SFloat;

            RenderingUtils.ReAllocateIfNeeded(
                ref backfaceDepthTexture,
                descriptor,
                FilterMode.Point,
                TextureWrapMode.Clamp,
                name: "_VolumeBackfaceDepthTexture");

            ConfigureTarget(backfaceDepthTexture);
            ConfigureClear(ClearFlag.All, Color.clear);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (backfaceDepthTexture == null)
            {
                return;
            }

            var drawingSettings = CreateDrawingSettings(
                ThicknessPassTags,
                ref renderingData,
                SortingCriteria.CommonOpaque);
            drawingSettings.perObjectData = PerObjectData.None;

            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                context.DrawRenderers(
                    renderingData.cullResults,
                    ref drawingSettings,
                    ref filteringSettings);

                cmd.SetGlobalTexture(VolumeBackfaceDepthTextureId, backfaceDepthTexture);
                cmd.SetGlobalFloat(VolumeThicknessAvailableId, 1.0f);

                // Preserve the original Jade globals while existing shaders migrate to the generic names.
                cmd.SetGlobalTexture(JadeBackfaceDepthTextureId, backfaceDepthTexture);
                cmd.SetGlobalFloat(JadeThicknessAvailableId, 1.0f);
                cmd.SetGlobalTexture(CrystalBackfaceDepthTextureId, backfaceDepthTexture);
                cmd.SetGlobalFloat(CrystalThicknessAvailableId, 1.0f);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            backfaceDepthTexture?.Release();
            backfaceDepthTexture = null;
        }
    }
}
