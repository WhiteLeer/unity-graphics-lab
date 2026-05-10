using UnityEngine;
using System;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public sealed class JadeVolumeVolumeController : MonoBehaviour
{
    private enum PreviewShape
    {
        Sphere = 0,
        Box = 1,
        Capsule = 2,
        JadeLump = 3
    }

    private const string DefaultAtlasResourcePath = "Generated/JadeVolume_PerlinDensityAtlas";
    private const string DefaultMaterialResourcePath = "Generated/M_JadeVolume_Object";

    [Header("光源")]
    [SerializeField, InspectorName("场景点光源")] private Light sourceLight;

    [Header("体纹理")]
    [SerializeField, InspectorName("物体材质")] private Material objectMaterial;
    [SerializeField, InspectorName("优先使用SD体纹理")] private bool preferSubstanceAtlas = true;
    [SerializeField, InspectorName("SD体纹理图集")] private Texture2D densityAtlas;
    [SerializeField, InspectorName("切片列数")] private int atlasColumns = 8;
    [SerializeField, InspectorName("切片行数")] private int atlasRows = 8;
    [SerializeField, InspectorName("体纹理分辨率")] private int textureResolution = 48;
    [SerializeField, InspectorName("体纹理刷新")] private bool regenerateTexture;
    [Header("预览形体")]
    [SerializeField, InspectorName("当前预制形状")] private PreviewShape previewShape = PreviewShape.JadeLump;
    [SerializeField, InspectorName("运行时允许切换")] private bool allowRuntimeShapeSwitch = true;
    [SerializeField, InspectorName("切换按键")] private KeyCode cycleShapeKey = KeyCode.Tab;
    [SerializeField, InspectorName("球体半径")] private float radius = 0.34f;
    [SerializeField, InspectorName("边缘软化")] private float edgeSoftness = 0.18f;
    [SerializeField, InspectorName("噪声强度")] private float noiseStrength = 0.22f;
    [SerializeField, InspectorName("噪声频率")] private float noiseFrequency = 5.0f;

    private static readonly int VolumeTexId = Shader.PropertyToID("_DensityTex");
    private static readonly int LightPositionId = Shader.PropertyToID("_VolumeLightPositionWS");
    private static readonly int LightColorId = Shader.PropertyToID("_VolumeLightColor");
    private static readonly int LightIntensityId = Shader.PropertyToID("_VolumeLightIntensity");
    private static readonly int VolumeBoundsScaleId = Shader.PropertyToID("_VolumeBoundsScale");
    private static readonly int ShapeModeId = Shader.PropertyToID("_ShapeMode");

    private MeshFilter meshFilter = null!;
    private MeshRenderer meshRenderer = null!;
    private MaterialPropertyBlock propertyBlock = null!;
    private Texture3D runtimeTexture;
    private Texture2D lastAtlasSource;
    private Hash128 lastAtlasHash;

    private void OnEnable()
    {
        Initialize();
        RebuildTextureIfNeeded(force: true);
        Apply();
    }

    private void Update()
    {
        HandleRuntimeInput();
        RebuildTextureIfNeeded(force: false);
        Apply();
    }

    private void OnValidate()
    {
        atlasColumns = Mathf.Clamp(atlasColumns, 1, 16);
        atlasRows = Mathf.Clamp(atlasRows, 1, 16);
        textureResolution = Mathf.Clamp(textureResolution, 16, 96);
        radius = Mathf.Clamp(radius, 0.05f, 0.49f);
        edgeSoftness = Mathf.Clamp(edgeSoftness, 0.01f, 0.4f);
        noiseStrength = Mathf.Clamp01(noiseStrength);
        noiseFrequency = Mathf.Clamp(noiseFrequency, 0.5f, 12.0f);

        if (!isActiveAndEnabled)
        {
            return;
        }

        regenerateTexture = true;
        RebuildTextureIfNeeded(force: true);
        Apply();
    }

    private void OnDisable()
    {
        if (runtimeTexture == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(runtimeTexture);
        }
        else
        {
            DestroyImmediate(runtimeTexture);
        }
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

        if (meshFilter.sharedMesh == null || meshFilter.sharedMesh.name.Contains("Runtime Mesh"))
        {
            meshFilter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        }

        var shader = Shader.Find("SurfaceLab/JadeVolume/VolumeObject");
        var targetMaterial = objectMaterial != null ? objectMaterial : Resources.Load<Material>(DefaultMaterialResourcePath);
        if (targetMaterial != null)
        {
            objectMaterial = targetMaterial;
            if (meshRenderer.sharedMaterial != targetMaterial)
            {
                meshRenderer.sharedMaterial = targetMaterial;
            }
        }
        else if (shader != null && meshRenderer.sharedMaterial == null)
        {
            meshRenderer.sharedMaterial = new Material(shader) { name = "M_JadeVolume_Object_Runtime" };
        }
    }

    private void HandleRuntimeInput()
    {
        if (!Application.isPlaying || !allowRuntimeShapeSwitch)
        {
            return;
        }

        if (Input.GetKeyDown(cycleShapeKey))
        {
            previewShape = (PreviewShape)(((int)previewShape + 1) % Enum.GetValues(typeof(PreviewShape)).Length);
        }

        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            previewShape = PreviewShape.Sphere;
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            previewShape = PreviewShape.Box;
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            previewShape = PreviewShape.Capsule;
        }
        else if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            previewShape = PreviewShape.JadeLump;
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

        var depth = atlasColumns * atlasRows;
        Color[] atlasPixels;
        try
        {
            atlasPixels = atlas.GetPixels();
        }
        catch (Exception)
        {
            return false;
        }
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

        RecreateTexture(sliceWidth, sliceHeight, depth, volumePixels, "JadeVolume_SD_Density3D");
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

        RecreateTexture(size, size, size, colors, "JadeVolume_RuntimeDensity3D");
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
        runtimeTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
    }

    private void Apply()
    {
        Initialize();
        var activeLight = ResolveLight();
        if (activeLight == null || runtimeTexture == null)
        {
            return;
        }

        meshRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetTexture(VolumeTexId, runtimeTexture);
        propertyBlock.SetVector(LightPositionId, activeLight.transform.position);
        propertyBlock.SetColor(LightColorId, activeLight.color.linear);
        propertyBlock.SetFloat(LightIntensityId, activeLight.intensity);
        propertyBlock.SetVector(VolumeBoundsScaleId, transform.lossyScale);
        propertyBlock.SetFloat(ShapeModeId, (float)previewShape);
        meshRenderer.SetPropertyBlock(propertyBlock);
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
