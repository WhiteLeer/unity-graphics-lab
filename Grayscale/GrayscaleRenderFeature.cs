using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class GrayscaleRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        public Material material;

        [Range(0f, 1f)] public float intensity = 1f;
    }

    public Settings settings = new Settings();
    private GrayscaleRenderPass pass;

    public override void Create()
    {
        pass = new GrayscaleRenderPass(settings);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.material == null)
        {
            Debug.LogWarning("Grayscale material is null!");
            return;
        }

        renderer.EnqueuePass(pass);
    }
}