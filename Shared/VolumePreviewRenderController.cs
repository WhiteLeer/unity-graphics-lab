using System;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
public class VolumePreviewRenderController : MonoBehaviour
{
    // Render/binding-only component.
    // Owns density texture generation, light binding, carrier activation, and shader property block application.
    // Carrier GameObjects must be created in the scene ahead of time.
    // Their mesh assets can be overridden from the scene profile.

    private const string DefaultAtlasResourcePath = "JadeVolume_PerlinDensityAtlas";
    private static readonly int VolumeTexId = Shader.PropertyToID("_DensityTex");
    private static readonly int LightPositionId = Shader.PropertyToID("_VolumeLightPositionWS");
    private static readonly int LightColorId = Shader.PropertyToID("_VolumeLightColor");
    private static readonly int LightIntensityId = Shader.PropertyToID("_VolumeLightIntensity");
    private static readonly int VolumeBoundsScaleId = Shader.PropertyToID("_VolumeBoundsScale");
    private static readonly int ShapeModeId = Shader.PropertyToID("_ShapeMode");
    private static readonly int PreviewPitchId = Shader.PropertyToID("_PreviewPitch");
    private static readonly int PreviewYawId = Shader.PropertyToID("_PreviewYaw");
    private static readonly int FullscreenQuadId = Shader.PropertyToID("_FullscreenQuad");

    [Header("光照")]
    [SerializeField, InspectorName("主光源")] private Light sourceLight;

    [Header("体积")]
    [SerializeField, InspectorName("优先使用材质图集")] private bool preferSubstanceAtlas = true;
    [SerializeField, InspectorName("密度图集")] private Texture2D densityAtlas;
    [SerializeField, InspectorName("图集列数")] private int atlasColumns = 8;
    [SerializeField, InspectorName("图集行数")] private int atlasRows = 8;
    [SerializeField, InspectorName("纹理分辨率")] private int textureResolution = 48;
    [SerializeField, InspectorName("重新生成纹理")] private bool regenerateTexture;

    [Header("载体")]
    [SerializeField, InspectorName("载体模式")] private VolumeMaterialPreviewController.CarrierMode carrierMode = VolumeMaterialPreviewController.CarrierMode.Cube;
    [SerializeField, InspectorName("全屏四边形模式")] private bool fullscreenQuadMode;
    [SerializeField, InspectorName("旧四边形载体")] private GameObject legacyQuadCarrierObject;
    [SerializeField, InspectorName("球体载体")] private GameObject sphereCarrierObject;
    [SerializeField, InspectorName("立方体载体")] private GameObject cubeCarrierObject;
    [SerializeField, InspectorName("胶囊体载体")] private GameObject capsuleCarrierObject;
    [SerializeField, InspectorName("挡位预制体载体")] private GameObject[] modeCarrierObjects = Array.Empty<GameObject>();

    [SerializeField, HideInInspector] private bool adoptedLegacySettings;

    private MeshRenderer rootRenderer = null!;
    private MeshRenderer legacyQuadCarrierRenderer = null!;
    private MeshRenderer sphereCarrierRenderer = null!;
    private MeshRenderer cubeCarrierRenderer = null!;
    private MeshRenderer capsuleCarrierRenderer = null!;
    private MeshFilter legacyQuadCarrierFilter = null!;
    private MeshFilter sphereCarrierFilter = null!;
    private MeshFilter cubeCarrierFilter = null!;
    private MeshFilter capsuleCarrierFilter = null!;
    private GameObject activeModeCarrierObject;
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
        activeModeCarrierObject = null;
        carrierMode = mode;
        fullscreenQuadMode = false;
        EnsureCarrierSelection();
    }

    public void SetFullscreenQuadMode(bool enabled)
    {
        if (enabled)
        {
            activeModeCarrierObject = null;
        }
        fullscreenQuadMode = enabled;
        EnsureCarrierSelection();
    }

    public void SetModeCarrierIndex(int modeIndex)
    {
        activeModeCarrierObject = modeCarrierObjects != null && modeIndex >= 0 && modeIndex < modeCarrierObjects.Length
            ? modeCarrierObjects[modeIndex]
            : null;
        EnsureCarrierSelection();
    }

    public void AlignFullscreenQuadToCamera(Camera previewCamera)
    {
        Initialize();
        EnsureCarrierSelection();

        if (!fullscreenQuadMode || legacyQuadCarrierObject == null || previewCamera == null)
        {
            return;
        }

        var quadTransform = legacyQuadCarrierObject.transform;
        var distance = Mathf.Max(previewCamera.nearClipPlane + 0.05f, 1f);
        var halfHeight = distance * Mathf.Tan(0.5f * previewCamera.fieldOfView * Mathf.Deg2Rad);
        var height = 2f * halfHeight * 1.05f;
        var width = height * Mathf.Max(previewCamera.aspect, 0.01f);
        quadTransform.SetPositionAndRotation(
            previewCamera.transform.position + previewCamera.transform.forward * distance,
            previewCamera.transform.rotation);
        quadTransform.localScale = new Vector3(width, height, 1f);
    }

    public void ApplyPreviewMesh(VolumePreviewSceneProfile profile, int modeIndex)
    {
        Initialize();
        EnsureCarrierSelection();

        if (fullscreenQuadMode || activeModeCarrierObject != null || profile == null)
        {
            return;
        }

        var activeCarrierObject = ResolveActiveCarrierObject();
        if (activeCarrierObject == null)
        {
            return;
        }

        var activeMesh = profile.GetPreviewMesh(modeIndex);
        if (activeMesh == null)
        {
            return;
        }

        var activeFilter = ResolveCarrierFilter(activeCarrierObject);
        if (activeFilter != null && activeFilter.sharedMesh != activeMesh)
        {
            activeFilter.sharedMesh = activeMesh;
        }
    }

    public Vector3 GetOrbitTargetPosition(Vector3 fallbackPosition)
    {
        Initialize();

        var activeCarrierObject = ResolveActiveCarrierObject();
        if (activeCarrierObject == null || fullscreenQuadMode)
        {
            return fallbackPosition;
        }

        var renderers = activeCarrierObject.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            return activeCarrierObject.transform.position;
        }

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds.center;
    }

    public float GetRecommendedOrbitDistance(Camera previewCamera)
    {
        Initialize();

        var activeCarrierObject = ResolveActiveCarrierObject();
        if (activeCarrierObject == null || fullscreenQuadMode || previewCamera == null || previewCamera.orthographic)
        {
            return -1.0f;
        }

        var renderers = activeCarrierObject.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            return -1.0f;
        }

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        var halfFov = 0.5f * previewCamera.fieldOfView * Mathf.Deg2Rad;
        var tangent = Mathf.Max(Mathf.Tan(halfFov), 0.001f);
        var aspect = Mathf.Max(previewCamera.aspect, 0.001f);
        var radius = bounds.extents.magnitude;
        var verticalDistance = radius / tangent;
        var horizontalDistance = radius / (tangent * aspect);
        return Mathf.Max(verticalDistance, horizontalDistance) * 1.25f;
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
        RebuildTextureIfNeeded(runtimeTexture == null);

        propertyBlock.Clear();
        if (runtimeTexture != null)
        {
            propertyBlock.SetTexture(VolumeTexId, runtimeTexture);
        }

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
        propertyBlock.SetFloat(FullscreenQuadId, fullscreenQuadMode ? 1.0f : 0.0f);
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

        if (activeModeCarrierObject != null)
        {
            foreach (var renderer in activeModeCarrierObject.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.sharedMaterial = activeMaterial;
                renderer.SetPropertyBlock(preparedPropertyBlock);
            }

            return;
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

        if (legacyQuadCarrierRenderer == null && legacyQuadCarrierObject != null)
        {
            legacyQuadCarrierRenderer = legacyQuadCarrierObject.GetComponent<MeshRenderer>();
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
            legacyQuadCarrierObject = transform.Find("Preview")?.gameObject;
        }

        if (legacyQuadCarrierObject == null)
        {
            legacyQuadCarrierObject = transform.Find("Preview_JadeVolume")?.gameObject;
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

        if (legacyQuadCarrierFilter == null && legacyQuadCarrierObject != null)
        {
            legacyQuadCarrierFilter = legacyQuadCarrierObject.GetComponent<MeshFilter>();
        }

        if (sphereCarrierFilter == null && sphereCarrierObject != null)
        {
            sphereCarrierFilter = sphereCarrierObject.GetComponent<MeshFilter>();
        }

        if (cubeCarrierFilter == null && cubeCarrierObject != null)
        {
            cubeCarrierFilter = cubeCarrierObject.GetComponent<MeshFilter>();
        }

        if (capsuleCarrierRenderer == null && capsuleCarrierObject != null)
        {
            capsuleCarrierRenderer = capsuleCarrierObject.GetComponent<MeshRenderer>();
        }

        if (capsuleCarrierFilter == null && capsuleCarrierObject != null)
        {
            capsuleCarrierFilter = capsuleCarrierObject.GetComponent<MeshFilter>();
        }

        var activeCarrierObject = ResolveActiveCarrierObject();
        SetCarrierActive(legacyQuadCarrierObject, fullscreenQuadMode);
        SetCarrierActive(sphereCarrierObject, activeCarrierObject == sphereCarrierObject);
        SetCarrierActive(cubeCarrierObject, activeCarrierObject == cubeCarrierObject);
        SetCarrierActive(capsuleCarrierObject, activeCarrierObject == capsuleCarrierObject);
        if (modeCarrierObjects != null)
        {
            foreach (var modeCarrierObject in modeCarrierObjects)
            {
                SetCarrierActive(modeCarrierObject, modeCarrierObject == activeModeCarrierObject && !fullscreenQuadMode);
            }
        }

        if (rootRenderer != null)
        {
            rootRenderer.enabled = !fullscreenQuadMode && sphereCarrierObject == null && cubeCarrierObject == null && capsuleCarrierObject == null;
        }
    }

    private MeshRenderer ResolveActiveCarrierRenderer()
    {
        if (fullscreenQuadMode)
        {
            return legacyQuadCarrierRenderer != null ? legacyQuadCarrierRenderer : rootRenderer;
        }

        if (activeModeCarrierObject != null)
        {
            return activeModeCarrierObject.GetComponentInChildren<MeshRenderer>(true);
        }

        return ResolveCarrierRenderer(carrierMode);
    }

    private GameObject ResolveActiveCarrierObject()
    {
        if (fullscreenQuadMode)
        {
            return legacyQuadCarrierObject;
        }

        return activeModeCarrierObject != null ? activeModeCarrierObject : ResolveCarrierObject(carrierMode);
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

    private MeshFilter ResolveCarrierFilter(GameObject carrierObject)
    {
        if (carrierObject == legacyQuadCarrierObject)
        {
            return legacyQuadCarrierFilter;
        }

        if (carrierObject == sphereCarrierObject)
        {
            return sphereCarrierFilter;
        }

        if (carrierObject == cubeCarrierObject)
        {
            return cubeCarrierFilter;
        }

        if (carrierObject == capsuleCarrierObject)
        {
            return capsuleCarrierFilter;
        }

        return carrierObject != null ? carrierObject.GetComponent<MeshFilter>() : null;
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
