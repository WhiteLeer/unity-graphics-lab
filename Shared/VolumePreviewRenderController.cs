using System;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class VolumePreviewRenderController : MonoBehaviour
{
    // Render/binding-only component.
    // Owns carrier mesh, density texture generation, light binding, and shader property block application.
    // It must not own preview mode, material index selection, or zoom persistence.
    // Effects may subclass this when they need custom resource generation or shader binding behavior.

    private const string DefaultAtlasResourcePath = "Generated/JadeVolume_PerlinDensityAtlas";
    private static Mesh quadCarrierPreviewMesh;
    private static Mesh cubeCarrierPreviewMesh;
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
    [SerializeField] private VolumeMaterialPreviewController.CarrierMode carrierMode = VolumeMaterialPreviewController.CarrierMode.Quad;
    [SerializeField] private float radius = 0.34f;
    [SerializeField] private float edgeSoftness = 0.18f;
    [SerializeField] private float noiseStrength = 0.22f;
    [SerializeField] private float noiseFrequency = 5.0f;

    [SerializeField, HideInInspector] private bool adoptedLegacySettings;

    private MeshFilter meshFilter = null!;
    private MeshRenderer meshRenderer = null!;
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
        radius = legacyRadius;
        edgeSoftness = legacyEdgeSoftness;
        noiseStrength = legacyNoiseStrength;
        noiseFrequency = legacyNoiseFrequency;
        adoptedLegacySettings = true;
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
        EnsureCarrierMesh();

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
        if (meshRenderer.sharedMaterial != activeMaterial)
        {
            meshRenderer.sharedMaterial = activeMaterial;
        }

        meshRenderer.SetPropertyBlock(preparedPropertyBlock);
    }

    private void Initialize()
    {
        if (meshFilter == null)
        {
            meshFilter = GetComponent<MeshFilter>();
        }

        if (meshRenderer == null)
        {
            meshRenderer = GetComponent<MeshRenderer>();
        }

        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }
    }

    private void EnsureCarrierMesh()
    {
        var carrierMesh = GetCarrierPreviewMesh(carrierMode);
        if (carrierMesh != null && meshFilter.sharedMesh != carrierMesh)
        {
            meshFilter.sharedMesh = carrierMesh;
        }
    }

    private static Mesh GetCarrierPreviewMesh(VolumeMaterialPreviewController.CarrierMode mode)
    {
        return mode == VolumeMaterialPreviewController.CarrierMode.Cube ? GetCubeCarrierPreviewMesh() : GetQuadCarrierPreviewMesh();
    }

    private static Mesh GetQuadCarrierPreviewMesh()
    {
        if (quadCarrierPreviewMesh != null)
        {
            return quadCarrierPreviewMesh;
        }

        quadCarrierPreviewMesh = new Mesh
        {
            name = "SharedPreview_QuadCarrier",
            hideFlags = HideFlags.HideAndDontSave
        };

        quadCarrierPreviewMesh.SetVertices(new[]
        {
            new Vector3(-0.5f, -0.5f, 0.0f),
            new Vector3(0.5f, -0.5f, 0.0f),
            new Vector3(0.5f, 0.5f, 0.0f),
            new Vector3(-0.5f, 0.5f, 0.0f)
        });
        quadCarrierPreviewMesh.SetUVs(0, new[]
        {
            new Vector2(0.0f, 0.0f),
            new Vector2(1.0f, 0.0f),
            new Vector2(1.0f, 1.0f),
            new Vector2(0.0f, 1.0f)
        });
        quadCarrierPreviewMesh.SetNormals(new[]
        {
            Vector3.forward,
            Vector3.forward,
            Vector3.forward,
            Vector3.forward
        });
        quadCarrierPreviewMesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0, true);
        quadCarrierPreviewMesh.RecalculateBounds();
        return quadCarrierPreviewMesh;
    }

    private static Mesh GetCubeCarrierPreviewMesh()
    {
        if (cubeCarrierPreviewMesh != null)
        {
            return cubeCarrierPreviewMesh;
        }

        var vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0.5f),  new Vector3(0.5f, -0.5f, 0.5f),   new Vector3(0.5f, 0.5f, 0.5f),    new Vector3(-0.5f, 0.5f, 0.5f),
            new Vector3(0.5f, -0.5f, -0.5f),  new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),  new Vector3(0.5f, 0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, 0.5f),  new Vector3(-0.5f, 0.5f, 0.5f),   new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(0.5f, -0.5f, 0.5f),   new Vector3(0.5f, -0.5f, -0.5f),  new Vector3(0.5f, 0.5f, -0.5f),   new Vector3(0.5f, 0.5f, 0.5f),
            new Vector3(-0.5f, 0.5f, 0.5f),   new Vector3(0.5f, 0.5f, 0.5f),    new Vector3(0.5f, 0.5f, -0.5f),   new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f),  new Vector3(0.5f, -0.5f, 0.5f),   new Vector3(-0.5f, -0.5f, 0.5f)
        };

        var normals = new[]
        {
            Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
            Vector3.back, Vector3.back, Vector3.back, Vector3.back,
            Vector3.left, Vector3.left, Vector3.left, Vector3.left,
            Vector3.right, Vector3.right, Vector3.right, Vector3.right,
            Vector3.up, Vector3.up, Vector3.up, Vector3.up,
            Vector3.down, Vector3.down, Vector3.down, Vector3.down
        };

        var uv = new[]
        {
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1)
        };

        var triangles = new[]
        {
            0, 1, 2, 0, 2, 3,
            4, 5, 6, 4, 6, 7,
            8, 9, 10, 8, 10, 11,
            12, 13, 14, 12, 14, 15,
            16, 17, 18, 16, 18, 19,
            20, 21, 22, 20, 22, 23
        };

        cubeCarrierPreviewMesh = new Mesh
        {
            name = "SharedPreview_CubeCarrier",
            hideFlags = HideFlags.HideAndDontSave
        };
        cubeCarrierPreviewMesh.SetVertices(vertices);
        cubeCarrierPreviewMesh.SetNormals(normals);
        cubeCarrierPreviewMesh.SetUVs(0, uv);
        cubeCarrierPreviewMesh.SetTriangles(triangles, 0, true);
        cubeCarrierPreviewMesh.RecalculateBounds();
        return cubeCarrierPreviewMesh;
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
                    var radial = 1f - Mathf.InverseLerp(radius, radius + edgeSoftness, distance);

                    var noiseA = Mathf.PerlinNoise((px + 0.5f) * noiseFrequency, (py + 0.5f) * noiseFrequency);
                    var noiseB = Mathf.PerlinNoise((py + 0.5f) * noiseFrequency * 1.37f, (pz + 0.5f) * noiseFrequency * 1.11f);
                    var noiseC = Mathf.PerlinNoise((px + 0.5f) * noiseFrequency * 0.89f, (pz + 0.5f) * noiseFrequency * 1.63f);
                    var combinedNoise = (noiseA + noiseB + noiseC) / 3f;

                    var density = Mathf.Clamp01(radial - noiseStrength * (combinedNoise - 0.5f));
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
