using System;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class VolumeMaterialPreviewController : MonoBehaviour
{
    public enum CarrierMode
    {
        Quad = 0,
        Cube = 1
    }

    public enum PreviewShape
    {
        Sphere = 0,
        Box = 1,
        Capsule = 2,
        JadeLump = 3
    }

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

    [Header("Materials")]
    [SerializeField] private Material[] previewMaterials = Array.Empty<Material>();
    [SerializeField] private int currentMaterialIndex;

    [Header("Volume")]
    [SerializeField] private bool preferSubstanceAtlas = true;
    [SerializeField] private Texture2D densityAtlas;
    [SerializeField] private int atlasColumns = 8;
    [SerializeField] private int atlasRows = 8;
    [SerializeField] private int textureResolution = 48;
    [SerializeField] private bool regenerateTexture;

    [Header("Shape")]
    [SerializeField] private CarrierMode carrierMode = CarrierMode.Quad;
    [SerializeField] private PreviewShape previewShape = PreviewShape.Sphere;
    [SerializeField] private bool allowRuntimeShapeSwitch = true;
    [SerializeField] private KeyCode cycleShapeKey = KeyCode.None;
    [SerializeField] private float radius = 0.34f;
    [SerializeField] private float edgeSoftness = 0.18f;
    [SerializeField] private float noiseStrength = 0.22f;
    [SerializeField] private float noiseFrequency = 5.0f;

    [Header("Interaction")]
    [SerializeField] private bool allowMouseRotate = true;
    [SerializeField] private int mouseButton = 0;
    [SerializeField] private float rotateSpeedX = 1.05f;
    [SerializeField] private float rotateSpeedY = 0.75f;
    [SerializeField] private Vector2 pitchLimits = new Vector2(-65f, 65f);
    [SerializeField] private float rotationSmoothTime = 0.12f;
    [SerializeField] private float rotationInertiaDamping = 4.5f;
    [SerializeField] private Camera previewCamera;
    [SerializeField] private bool allowScrollZoom = true;
    [SerializeField] private float zoomSpeed = 2.4f;
    [SerializeField] private Vector2 zoomDistanceLimits = new Vector2(1.2f, 4.5f);
    [SerializeField] private bool applyTransformRotation = true;

    private MeshFilter meshFilter = null!;
    private MeshRenderer meshRenderer = null!;
    private MaterialPropertyBlock propertyBlock = null!;
    private Texture3D runtimeTexture;
    private Texture2D lastAtlasSource;
    private Hash128 lastAtlasHash;
    private float pitch = 12f;
    private float yaw = -24f;
    private float targetPitch = 12f;
    private float targetYaw = -24f;
    private float pitchVelocity;
    private float yawVelocity;
    private float zoomDistance = 2.6f;
    private float targetZoomDistance = 2.6f;
    private float zoomVelocity;

    protected virtual PreviewShape DefaultPreviewShape => PreviewShape.Sphere;

    private void OnEnable()
    {
        previewShape = DefaultPreviewShape;
        Initialize();
        CacheCameraDefaults();
        RebuildTextureIfNeeded(true);
        Apply();
    }

    private void Update()
    {
        HandleRuntimeInput();
        RebuildTextureIfNeeded(false);
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
        currentMaterialIndex = Mathf.Clamp(currentMaterialIndex, 0, Mathf.Max(0, previewMaterials.Length - 1));
        pitchLimits.x = Mathf.Clamp(pitchLimits.x, -89f, 89f);
        pitchLimits.y = Mathf.Clamp(pitchLimits.y, pitchLimits.x, 89f);
        pitch = Mathf.Clamp(pitch, pitchLimits.x, pitchLimits.y);
        targetPitch = Mathf.Clamp(targetPitch, pitchLimits.x, pitchLimits.y);
        zoomDistanceLimits.x = Mathf.Max(0.1f, zoomDistanceLimits.x);
        zoomDistanceLimits.y = Mathf.Max(zoomDistanceLimits.x, zoomDistanceLimits.y);
        zoomDistance = Mathf.Clamp(zoomDistance, zoomDistanceLimits.x, zoomDistanceLimits.y);
        targetZoomDistance = Mathf.Clamp(targetZoomDistance, zoomDistanceLimits.x, zoomDistanceLimits.y);

        if (!isActiveAndEnabled)
        {
            return;
        }

        regenerateTexture = true;
        RebuildTextureIfNeeded(true);
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

    public void SetMaterialIndex(int index)
    {
        currentMaterialIndex = Mathf.Clamp(index, 0, Mathf.Max(0, previewMaterials.Length - 1));
        Initialize();
        Apply();
    }

    public void SetPreviewShape(PreviewShape shape)
    {
        previewShape = shape;
        Apply();
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

        var carrierMesh = GetCarrierPreviewMesh(carrierMode);
        if (carrierMesh != null && meshFilter.sharedMesh != carrierMesh)
        {
            meshFilter.sharedMesh = carrierMesh;
        }

        var activeMaterial = ResolveActiveMaterial();
        if (activeMaterial != null && meshRenderer.sharedMaterial != activeMaterial)
        {
            meshRenderer.sharedMaterial = activeMaterial;
        }
    }

    private static Mesh GetCarrierPreviewMesh(CarrierMode mode)
    {
        return mode == CarrierMode.Cube ? GetCubeCarrierPreviewMesh() : GetQuadCarrierPreviewMesh();
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

    private void HandleRuntimeInput()
    {
        if (!Application.isPlaying || !allowRuntimeShapeSwitch)
        {
            HandleMouseRotate();
            return;
        }

        if (cycleShapeKey != KeyCode.None && Input.GetKeyDown(cycleShapeKey))
        {
            previewShape = NextRuntimeShape(previewShape);
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

        HandleMouseRotate();
    }

    private static PreviewShape NextRuntimeShape(PreviewShape shape)
    {
        return shape switch
        {
            PreviewShape.Sphere => PreviewShape.Box,
            PreviewShape.Box => PreviewShape.Capsule,
            _ => PreviewShape.Sphere
        };
    }

    private void HandleMouseRotate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (allowMouseRotate && Input.GetMouseButton(mouseButton))
        {
            targetPitch = Mathf.Clamp(
                targetPitch - Input.GetAxis("Mouse Y") * rotateSpeedY * 20.0f,
                pitchLimits.x,
                pitchLimits.y
            );
            targetYaw += Input.GetAxis("Mouse X") * rotateSpeedX * 20.0f;
        }

        pitch = Mathf.SmoothDampAngle(pitch, targetPitch, ref pitchVelocity, rotationSmoothTime);
        yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVelocity, rotationSmoothTime);

        if (!Input.GetMouseButton(mouseButton))
        {
            var damping = Mathf.Exp(-rotationInertiaDamping * Mathf.Max(Time.unscaledDeltaTime, 0.0001f));
            targetPitch += pitchVelocity * Time.unscaledDeltaTime;
            targetYaw += yawVelocity * Time.unscaledDeltaTime;
            pitchVelocity *= damping;
            yawVelocity *= damping;
            targetPitch = Mathf.Clamp(targetPitch, pitchLimits.x, pitchLimits.y);
        }

        if (applyTransformRotation)
        {
            transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, Vector3.right);
        }
        else
        {
            transform.rotation = Quaternion.identity;
        }

        HandleScrollZoom();
    }

    private void HandleScrollZoom()
    {
        if (!allowScrollZoom)
        {
            return;
        }

        var cam = ResolvePreviewCamera();
        if (cam == null || cam.orthographic)
        {
            return;
        }

        var scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 1e-4f)
        {
            targetZoomDistance = Mathf.Clamp(targetZoomDistance - scroll * zoomSpeed, zoomDistanceLimits.x, zoomDistanceLimits.y);
        }

        zoomDistance = Mathf.SmoothDamp(zoomDistance, targetZoomDistance, ref zoomVelocity, 0.08f);
        var local = cam.transform.localPosition;
        local.z = -zoomDistance;
        cam.transform.localPosition = local;
    }

    private void CacheCameraDefaults()
    {
        var cam = ResolvePreviewCamera();
        if (cam == null)
        {
            return;
        }

        zoomDistance = Mathf.Clamp(Mathf.Abs(cam.transform.localPosition.z), zoomDistanceLimits.x, zoomDistanceLimits.y);
        targetZoomDistance = zoomDistance;
    }

    private Camera ResolvePreviewCamera()
    {
        if (previewCamera != null)
        {
            return previewCamera;
        }

        if (Camera.main != null)
        {
            previewCamera = Camera.main;
        }

        return previewCamera;
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

    private void Apply()
    {
        Initialize();

        var activeLight = ResolveLight();
        var activeMaterial = ResolveActiveMaterial();
        if (activeMaterial == null || activeLight == null || runtimeTexture == null)
        {
            return;
        }

        if (meshRenderer.sharedMaterial != activeMaterial)
        {
            meshRenderer.sharedMaterial = activeMaterial;
        }

        meshRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetTexture(VolumeTexId, runtimeTexture);
        propertyBlock.SetVector(LightPositionId, activeLight.transform.position);
        propertyBlock.SetColor(LightColorId, activeLight.color.linear);
        propertyBlock.SetFloat(LightIntensityId, activeLight.intensity);
        propertyBlock.SetVector(VolumeBoundsScaleId, transform.lossyScale);
        propertyBlock.SetFloat(ShapeModeId, (float)previewShape);
        propertyBlock.SetFloat(PreviewPitchId, pitch);
        propertyBlock.SetFloat(PreviewYawId, yaw);
        meshRenderer.SetPropertyBlock(propertyBlock);
    }

    private Material ResolveActiveMaterial()
    {
        if (previewMaterials == null || previewMaterials.Length == 0)
        {
            return null;
        }

        currentMaterialIndex = Mathf.Clamp(currentMaterialIndex, 0, previewMaterials.Length - 1);
        return previewMaterials[currentMaterialIndex];
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
