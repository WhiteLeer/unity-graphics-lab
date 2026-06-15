using System;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
public class VolumePreviewRenderController : MonoBehaviour
{
    // Render/binding-only component.
    // Owns density texture generation, light binding, carrier activation, and shader property block application.
    // Carrier meshes must be created in the scene ahead of time.

    private const string DefaultAtlasResourcePath = "Generated/JadeVolume_PerlinDensityAtlas";
    private static readonly int VolumeTexId = Shader.PropertyToID("_DensityTex");
    private static readonly int LightPositionId = Shader.PropertyToID("_VolumeLightPositionWS");
    private static readonly int LightColorId = Shader.PropertyToID("_VolumeLightColor");
    private static readonly int LightIntensityId = Shader.PropertyToID("_VolumeLightIntensity");
    private static readonly int VolumeBoundsScaleId = Shader.PropertyToID("_VolumeBoundsScale");
    private static readonly int ShapeModeId = Shader.PropertyToID("_ShapeMode");
    private static readonly int PreviewPitchId = Shader.PropertyToID("_PreviewPitch");
    private static readonly int PreviewYawId = Shader.PropertyToID("_PreviewYaw");

    [Header("Light")]
    [SerializeField] private Light sourceLight;

    [Header("Volume")]
    [SerializeField] private bool preferSubstanceAtlas = true;
    [SerializeField] private Texture2D densityAtlas;
    [SerializeField] private int atlasColumns = 8;
    [SerializeField] private int atlasRows = 8;
    [SerializeField] private int textureResolution = 48;
    [SerializeField] private bool regenerateTexture;

    [Header("Carrier")]
    [SerializeField] private VolumeMaterialPreviewController.CarrierMode carrierMode = VolumeMaterialPreviewController.CarrierMode.Cube;
    [SerializeField] private GameObject legacyQuadCarrierObject;
    [SerializeField] private GameObject sphereCarrierObject;
    [SerializeField] private GameObject cubeCarrierObject;
    [SerializeField] private GameObject capsuleCarrierObject;

    [SerializeField, HideInInspector] private bool adoptedLegacySettings;

    private MeshRenderer rootRenderer = null!;
    private MeshRenderer sphereCarrierRenderer = null!;
    private MeshRenderer cubeCarrierRenderer = null!;
    private MeshRenderer capsuleCarrierRenderer = null!;
    private MaterialPropertyBlock propertyBlock = null!;
    private Texture3D runtimeTexture;
    private Texture2D lastAtlasSource;
    private Hash128 lastAtlasHash;

    public void AdoptLegacySettings(
        Light legacySourceLight,
        bool legacyPreferSubstanceAtlas,
        Texture2D legacyDensityAtlas,
        int legacyAtlasColumns,
        int legacyAtlasRows,
        int legacyTextureResolution,
        bool legacyRegenerateTexture,
        VolumeMaterialPreviewController.CarrierMode legacyCarrierMode,
        float legacyRadius,
        float legacyEdgeSoftness,
        float legacyNoiseStrength,
        float legacyNoiseFrequency)
    {
        if (adoptedLegacySettings)
        {
            return;
        }

        sourceLight = legacySourceLight;
        preferSubstanceAtlas = legacyPreferSubstanceAtlas;
        densityAtlas = legacyDensityAtlas;
        atlasColumns = legacyAtlasColumns;
        atlasRows = legacyAtlasRows;
        textureResolution = legacyTextureResolution;
        regenerateTexture = legacyRegenerateTexture;
        carrierMode = legacyCarrierMode;
        adoptedLegacySettings = true;
    }

    public void SetCarrierMode(VolumeMaterialPreviewController.CarrierMode mode)
    {
        carrierMode = mode;
        EnsureCarrierSelection();
    }

    public void TickResources(bool force)
    {
        Initialize();
        RebuildTextureIfNeeded(force);
    }

    public MaterialPropertyBlock PreparePropertyBlock(
        Material activeMaterial,
        Transform previewTransform,
        VolumeMaterialPreviewController.PreviewShape previewShape,
        bool overrideMaterialShapeMode,
        float previewPitch,
        float previewYaw)
    {
        Initialize();
        EnsureCarrierSelection();

        propertyBlock.Clear();
        propertyBlock.SetTexture(VolumeTexId, runtimeTexture);

        var activeLight = ResolveLight();
        if (activeLight != null)
        {
            propertyBlock.SetVector(LightPositionId, activeLight.transform.position);
            propertyBlock.SetColor(LightColorId, activeLight.color.linear);
            propertyBlock.SetFloat(LightIntensityId, activeLight.intensity);
        }

        propertyBlock.SetVector(VolumeBoundsScaleId, previewTransform.lossyScale);
        if (overrideMaterialShapeMode)
        {
            propertyBlock.SetFloat(ShapeModeId, (float)previewShape);
        }
        else if (activeMaterial.HasProperty(ShapeModeId))
        {
            propertyBlock.SetFloat(ShapeModeId, activeMaterial.GetFloat(ShapeModeId));
        }

        propertyBlock.SetFloat(PreviewPitchId, previewPitch);
        propertyBlock.SetFloat(PreviewYawId, previewYaw);
        return propertyBlock;
    }

    public void ApplyPreparedPropertyBlock(Material activeMaterial, MaterialPropertyBlock preparedPropertyBlock)
    {
        Initialize();
        var activeRenderer = ResolveActiveCarrierRenderer();
        if (activeRenderer == null)
        {
            activeRenderer = rootRenderer;
        }

        if (activeRenderer != null && activeRenderer.sharedMaterial != activeMaterial)
        {
            activeRenderer.sharedMaterial = activeMaterial;
        }

        if (activeRenderer != null)
        {
            activeRenderer.SetPropertyBlock(preparedPropertyBlock);
        }
    }

    private void Initialize()
    {
        if (rootRenderer == null)
        {
            rootRenderer = GetComponent<MeshRenderer>();
        }

        if (cubeCarrierRenderer == null && cubeCarrierObject != null)
        {
            cubeCarrierRenderer = cubeCarrierObject.GetComponent<MeshRenderer>();
        }

        if (capsuleCarrierRenderer == null && capsuleCarrierObject != null)
        {
            capsuleCarrierRenderer = capsuleCarrierObject.GetComponent<MeshRenderer>();
        }

        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }
    }

    private void EnsureCarrierSelection()
    {
        if (sphereCarrierObject == null)
        {
            sphereCarrierObject = transform.Find("PreviewCarrier_Sphere")?.gameObject;
        }

        if (legacyQuadCarrierObject == null)
        {
            legacyQuadCarrierObject = transform.Find("PreviewCarrier_Quad")?.gameObject;
        }

        if (cubeCarrierObject == null)
        {
            cubeCarrierObject = transform.Find("PreviewCarrier_Cube")?.gameObject;
        }

        if (capsuleCarrierObject == null)
        {
            capsuleCarrierObject = transform.Find("PreviewCarrier_Capsule")?.gameObject;
        }

        if (sphereCarrierRenderer == null && sphereCarrierObject != null)
        {
            sphereCarrierRenderer = sphereCarrierObject.GetComponent<MeshRenderer>();
        }

        if (cubeCarrierRenderer == null && cubeCarrierObject != null)
        {
            cubeCarrierRenderer = cubeCarrierObject.GetComponent<MeshRenderer>();
        }

        if (capsuleCarrierRenderer == null && capsuleCarrierObject != null)
        {
            capsuleCarrierRenderer = capsuleCarrierObject.GetComponent<MeshRenderer>();
        }

        var activeCarrierObject = ResolveActiveCarrierObject();
        SetCarrierActive(legacyQuadCarrierObject, false);
        SetCarrierActive(sphereCarrierObject, activeCarrierObject == sphereCarrierObject);
        SetCarrierActive(cubeCarrierObject, activeCarrierObject == cubeCarrierObject);
        SetCarrierActive(capsuleCarrierObject, activeCarrierObject == capsuleCarrierObject);

        if (rootRenderer != null)
        {
            rootRenderer.enabled = sphereCarrierObject == null && cubeCarrierObject == null && capsuleCarrierObject == null;
        }
    }

    private MeshRenderer ResolveActiveCarrierRenderer()
    {
        return ResolveCarrierRenderer(carrierMode);
    }

    private GameObject ResolveActiveCarrierObject()
    {
        return ResolveCarrierObject(carrierMode);
    }

    private MeshRenderer ResolveCarrierRenderer(VolumeMaterialPreviewController.CarrierMode mode)
    {
        return mode switch
        {
            VolumeMaterialPreviewController.CarrierMode.Sphere => sphereCarrierRenderer,
            VolumeMaterialPreviewController.CarrierMode.Cube => cubeCarrierRenderer,
            VolumeMaterialPreviewController.CarrierMode.Capsule => capsuleCarrierRenderer,
            _ => cubeCarrierRenderer
        };
    }

    private GameObject ResolveCarrierObject(VolumeMaterialPreviewController.CarrierMode mode)
    {
        return mode switch
        {
            VolumeMaterialPreviewController.CarrierMode.Sphere => sphereCarrierObject,
            VolumeMaterialPreviewController.CarrierMode.Cube => cubeCarrierObject,
            VolumeMaterialPreviewController.CarrierMode.Capsule => capsuleCarrierObject,
            _ => cubeCarrierObject
        };
    }

    private static void SetCarrierActive(GameObject carrierObject, bool active)
    {
        if (carrierObject != null)
        {
            carrierObject.SetActive(active);
        }
    }

    private void RebuildTextureIfNeeded(bool force)
    {
        var atlas = ResolveAtlas();
        var atlasChanged = HasAtlasChanged(atlas);
        if (!force && !regenerateTexture && !atlasChanged && runtimeTexture != null)
        {
            return;
        }

        regenerateTexture = false;
        BuildTexture(atlas);
    }

    private void BuildTexture(Texture2D atlas)
    {
        if (TryBuildTextureFromAtlas(atlas))
        {
            return;
        }

        BuildProceduralTexture();
    }

    private Texture2D ResolveAtlas()
    {
        if (!preferSubstanceAtlas)
        {
            return null;
        }

        return densityAtlas != null ? densityAtlas : Resources.Load<Texture2D>(DefaultAtlasResourcePath);
    }

    private bool HasAtlasChanged(Texture2D atlas)
    {
        if (!preferSubstanceAtlas)
        {
            return lastAtlasSource != null;
        }

        if (atlas == null)
        {
            return lastAtlasSource != null;
        }

        return runtimeTexture == null || atlas != lastAtlasSource || atlas.imageContentsHash != lastAtlasHash;
    }

    private bool TryBuildTextureFromAtlas(Texture2D atlas)
    {
        if (atlas == null)
        {
            return false;
        }

        var sliceWidth = atlas.width / atlasColumns;
        var sliceHeight = atlas.height / atlasRows;
        if (sliceWidth <= 0 || sliceHeight <= 0 || sliceWidth != sliceHeight)
        {
            return false;
        }

        Color[] atlasPixels;
        try
        {
            atlasPixels = atlas.GetPixels();
        }
        catch (Exception)
        {
            return false;
        }

        var depth = atlasColumns * atlasRows;
        var volumePixels = new Color[sliceWidth * sliceHeight * depth];
        var writeIndex = 0;

        for (var slice = 0; slice < depth; slice++)
        {
            var tileX = slice % atlasColumns;
            var tileY = slice / atlasColumns;
            var originX = tileX * sliceWidth;
            var originY = (atlasRows - 1 - tileY) * sliceHeight;

            for (var y = 0; y < sliceHeight; y++)
            {
                for (var x = 0; x < sliceWidth; x++)
                {
                    var atlasIndex = (originY + (sliceHeight - 1 - y)) * atlas.width + originX + x;
                    var density = atlasPixels[atlasIndex].grayscale;
                    volumePixels[writeIndex++] = new Color(density, density, density, density);
                }
            }
        }

        RecreateTexture(sliceWidth, sliceHeight, depth, volumePixels, "SharedPreview_SD_Density3D");
        textureResolution = sliceWidth;
        lastAtlasSource = atlas;
        lastAtlasHash = atlas.imageContentsHash;
        return true;
    }

    private void BuildProceduralTexture()
    {
        var size = textureResolution;
        var colors = new Color[size * size * size];
        var index = 0;

        for (var z = 0; z < size; z++)
        {
            var pz = Mathf.Lerp(-0.5f, 0.5f, z / (float)(size - 1));
            for (var y = 0; y < size; y++)
            {
                var py = Mathf.Lerp(-0.5f, 0.5f, y / (float)(size - 1));
                for (var x = 0; x < size; x++)
                {
                    var px = Mathf.Lerp(-0.5f, 0.5f, x / (float)(size - 1));
                    var distance = new Vector3(px, py, pz).magnitude;
                    var radial = 1f - Mathf.InverseLerp(0.34f, 0.34f + 0.18f, distance);

                    var noiseA = Mathf.PerlinNoise((px + 0.5f) * 5.0f, (py + 0.5f) * 5.0f);
                    var noiseB = Mathf.PerlinNoise((py + 0.5f) * 5.0f * 1.37f, (pz + 0.5f) * 5.0f * 1.11f);
                    var noiseC = Mathf.PerlinNoise((px + 0.5f) * 5.0f * 0.89f, (pz + 0.5f) * 5.0f * 1.63f);
                    var combinedNoise = (noiseA + noiseB + noiseC) / 3f;

                    var density = Mathf.Clamp01(radial - 0.22f * (combinedNoise - 0.5f));
                    colors[index++] = new Color(density, density, density, density);
                }
            }
        }

        RecreateTexture(size, size, size, colors, "SharedPreview_RuntimeDensity3D");
        lastAtlasSource = null;
        lastAtlasHash = default;
    }

    private void RecreateTexture(int width, int height, int depth, Color[] colors, string textureName)
    {
        if (runtimeTexture != null)
        {
            if (Application.isPlaying)
            {
                Destroy(runtimeTexture);
            }
            else
            {
                DestroyImmediate(runtimeTexture);
            }
        }

        runtimeTexture = new Texture3D(width, height, depth, TextureFormat.RGBA32, false)
        {
            name = textureName,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear
        };
        runtimeTexture.SetPixels(colors);
        runtimeTexture.Apply(false, false);
    }

    private Light ResolveLight()
    {
        if (sourceLight != null && sourceLight.enabled && sourceLight.type == LightType.Point)
        {
            return sourceLight;
        }

        var lights = FindObjectsOfType<Light>();
        foreach (var lightComponent in lights)
        {
            if (lightComponent.enabled && lightComponent.type == LightType.Point)
            {
                return lightComponent;
            }
        }

        return null;
    }
}
