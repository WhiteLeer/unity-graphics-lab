using System;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class VolumeMaterialPreviewController : MonoBehaviour
{
    // Ownership contract for preview state:
    // 1. This controller owns only high-level preview state: runtime shape and active material index.
    // 2. PreviewInteractionController owns runtime rotation and zoom.
    // 3. VolumePreviewRenderController owns prebuilt carrier activation, density texture generation, and shader property binding.
    // 4. ReferencePreviewToggleController owns only reference/preview mode switching and per-mode zoom persistence.
    // 5. Material _ShapeMode may seed runtime shape during material changes, but it is not a second live owner.
    //
    // If you need new behavior, decide which owner should change. Do not add side writes in random scene helpers.
    public enum CarrierMode
    {
        Sphere = 0,
        Cube = 1,
        Capsule = 2
    }

    public enum PreviewShape
    {
        Sphere = 0,
        Box = 1,
        Capsule = 2,
        JadeLump = 3
    }

    [SerializeField] private int currentMaterialIndex;

    [Header("Shape")]
    [SerializeField] private PreviewShape previewShape = PreviewShape.Sphere;
    [SerializeField] private bool allowRuntimeShapeSwitch = true;
    [SerializeField] private KeyCode cycleShapeKey = KeyCode.None;

    [Header("Helpers")]
    [SerializeField] private PreviewInteractionController interactionController;
    [SerializeField] private VolumePreviewRenderController renderController;

    [HideInInspector]
    [SerializeField] private VolumePreviewSceneProfile templateProfile;

    // Legacy serialized settings retained only to migrate existing scene data into the new helper components.
    [Header("Legacy Migration")]
    [SerializeField, HideInInspector] private Light sourceLight;
    [SerializeField, HideInInspector] private bool preferSubstanceAtlas = true;
    [SerializeField, HideInInspector] private Texture2D densityAtlas;
    [SerializeField, HideInInspector] private int atlasColumns = 8;
    [SerializeField, HideInInspector] private int atlasRows = 8;
    [SerializeField, HideInInspector] private int textureResolution = 48;
    [SerializeField, HideInInspector] private bool regenerateTexture;
    [SerializeField, HideInInspector] private CarrierMode carrierMode = CarrierMode.Cube;
    [SerializeField, HideInInspector] private float radius = 0.34f;
    [SerializeField, HideInInspector] private float edgeSoftness = 0.18f;
    [SerializeField, HideInInspector] private float noiseStrength = 0.22f;
    [SerializeField, HideInInspector] private float noiseFrequency = 5.0f;
    [SerializeField, HideInInspector] private bool allowMouseRotate = true;
    [SerializeField, HideInInspector] private int mouseButton = 0;
    [SerializeField, HideInInspector] private float rotateSpeedX = 1.05f;
    [SerializeField, HideInInspector] private float rotateSpeedY = 0.75f;
    [SerializeField, HideInInspector] private Vector2 pitchLimits = new Vector2(-65f, 65f);
    [SerializeField, HideInInspector] private float rotationSmoothTime = 0.12f;
    [SerializeField, HideInInspector] private float rotationInertiaDamping = 4.5f;
    [SerializeField, HideInInspector] private Camera previewCamera;
    [SerializeField, HideInInspector] private bool allowScrollZoom = true;
    [SerializeField, HideInInspector] private float zoomSpeed = 2.4f;
    [SerializeField, HideInInspector] private Vector2 zoomDistanceLimits = new Vector2(1.2f, 4.5f);
    [SerializeField, HideInInspector] private bool applyTransformRotation = true;

    [SerializeField, HideInInspector] private bool previewStateInitialized;

    private static readonly int ShapeModeId = Shader.PropertyToID("_ShapeMode");

    protected virtual PreviewShape DefaultPreviewShape => PreviewShape.Sphere;
    protected virtual bool OverrideMaterialShapeMode => true;
    protected virtual bool SyncShapeFromMaterialOnMaterialChange => false;
    protected virtual Type PreferredInteractionControllerType => typeof(PreviewInteractionController);
    protected virtual Type PreferredRenderControllerType => typeof(VolumePreviewRenderController);

    private void OnEnable()
    {
        EnsureHelperComponents(true);
        MigrateLegacySettingsIfNeeded();
        InitializePreviewStateOnce();
        interactionController.CacheCameraDefaults();
        renderController.TickResources(true);
        Apply();
    }

    private void Update()
    {
        HandleRuntimeShapeInput();
        interactionController.Tick(transform);
        renderController.TickResources(false);
        Apply();
    }

    private void OnValidate()
    {
        currentMaterialIndex = Mathf.Clamp(currentMaterialIndex, 0, Mathf.Max(0, GetPreviewMaterialCount() - 1));
        EnsureHelperComponents(false);
        MigrateLegacySettingsIfNeeded();

        if (!isActiveAndEnabled || interactionController == null || renderController == null)
        {
            return;
        }

        renderController.TickResources(true);
        Apply();
    }

    public void SetMaterialIndex(int index)
    {
        currentMaterialIndex = Mathf.Clamp(index, 0, Mathf.Max(0, GetPreviewMaterialCount() - 1));
        if (SyncShapeFromMaterialOnMaterialChange)
        {
            SyncPreviewShapeFromActiveMaterial();
        }

        Apply();
    }

    public void SetCarrierMode(CarrierMode mode)
    {
        EnsureHelperComponents(true);
        renderController.SetCarrierMode(mode);
        Apply();
    }

    public void SetPreviewShape(PreviewShape shape)
    {
        previewShape = shape;
        carrierMode = MapPreviewShapeToCarrierMode(shape);
        previewStateInitialized = true;
        renderController.SetCarrierMode(carrierMode);
        Apply();
    }

    public PreviewShape GetPreviewShape()
    {
        return previewShape;
    }

    public int GetMaterialIndex()
    {
        return currentMaterialIndex;
    }

    public int GetPreviewMaterialCount()
    {
        if (templateProfile != null && templateProfile.PreviewModeCount > 0)
        {
            return templateProfile.PreviewModeCount;
        }

        return 0;
    }

    public float GetPreviewZoomDistance()
    {
        EnsureHelperComponents(true);
        return interactionController.GetPreviewZoomDistance();
    }

    public void SetPreviewZoomDistance(float distance)
    {
        EnsureHelperComponents(true);
        interactionController.SetPreviewZoomDistance(distance);
    }

    public void SetSceneProfile(VolumePreviewSceneProfile profile)
    {
        templateProfile = profile;
        MigrateLegacySettingsIfNeeded();

        if (isActiveAndEnabled)
        {
            Apply();
        }
    }

    protected virtual void BeforeApplyRenderProperties(MaterialPropertyBlock propertyBlock, Material activeMaterial)
    {
    }

    private void InitializePreviewStateOnce()
    {
        if (previewStateInitialized)
        {
            return;
        }

        previewShape = DefaultPreviewShape;
        previewStateInitialized = true;
    }

    private void EnsureHelperComponents(bool allowAdd)
    {
        if (interactionController == null)
        {
            interactionController = GetComponent<PreviewInteractionController>();
            if (interactionController == null && allowAdd)
            {
                interactionController = (PreviewInteractionController)gameObject.AddComponent(PreferredInteractionControllerType);
            }
        }

        if (renderController == null)
        {
            renderController = GetComponent<VolumePreviewRenderController>();
            if (renderController == null && allowAdd)
            {
                renderController = (VolumePreviewRenderController)gameObject.AddComponent(PreferredRenderControllerType);
            }
        }
    }

    private void MigrateLegacySettingsIfNeeded()
    {
        if (interactionController == null || renderController == null)
        {
            return;
        }

        var profile = templateProfile;
        interactionController.AdoptLegacySettings(
            previewCamera,
            profile != null ? profile.AllowMouseRotate : allowMouseRotate,
            profile != null ? profile.MouseButton : mouseButton,
            profile != null ? profile.RotateSpeedX : rotateSpeedX,
            profile != null ? profile.RotateSpeedY : rotateSpeedY,
            profile != null ? profile.PitchLimits : pitchLimits,
            profile != null ? profile.RotationSmoothTime : rotationSmoothTime,
            profile != null ? profile.RotationInertiaDamping : rotationInertiaDamping,
            profile != null ? profile.AllowScrollZoom : allowScrollZoom,
            profile != null ? profile.ZoomSpeed : zoomSpeed,
            profile != null ? profile.ZoomDistanceLimits : zoomDistanceLimits,
            profile != null ? profile.ApplyTransformRotation : applyTransformRotation);

        allowRuntimeShapeSwitch = profile != null ? profile.AllowRuntimeShapeSwitch : allowRuntimeShapeSwitch;
        cycleShapeKey = profile != null ? profile.CycleShapeKey : cycleShapeKey;

        renderController.AdoptLegacySettings(
            sourceLight,
            preferSubstanceAtlas,
            densityAtlas,
            atlasColumns,
            atlasRows,
            textureResolution,
            regenerateTexture,
            carrierMode,
            radius,
            edgeSoftness,
            noiseStrength,
            noiseFrequency);
    }

    private void HandleRuntimeShapeInput()
    {
        // Shape hotkeys modify the controller-owned runtime shape only.
        if (allowRuntimeShapeSwitch && cycleShapeKey != KeyCode.None && Input.GetKeyDown(cycleShapeKey))
        {
            SetPreviewShape(NextRuntimeShape(previewShape));
        }

        if (allowRuntimeShapeSwitch && Input.GetKeyDown(KeyCode.Alpha1))
        {
            SetPreviewShape(PreviewShape.Sphere);
        }
        else if (allowRuntimeShapeSwitch && Input.GetKeyDown(KeyCode.Alpha2))
        {
            SetPreviewShape(PreviewShape.Box);
        }
        else if (allowRuntimeShapeSwitch && Input.GetKeyDown(KeyCode.Alpha3))
        {
            SetPreviewShape(PreviewShape.Capsule);
        }
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

    private void SyncPreviewShapeFromActiveMaterial()
    {
        var activeMaterial = ResolveActiveMaterial();
        if (activeMaterial == null || !activeMaterial.HasProperty(ShapeModeId))
        {
            return;
        }

        var materialShape = Mathf.RoundToInt(activeMaterial.GetFloat(ShapeModeId));
        materialShape = Mathf.Clamp(materialShape, 0, (int)PreviewShape.JadeLump);
        previewShape = (PreviewShape)materialShape;
        previewStateInitialized = true;
    }

    private Material ResolveActiveMaterial()
    {
        if (templateProfile != null && templateProfile.PreviewModeCount > 0)
        {
            currentMaterialIndex = Mathf.Clamp(currentMaterialIndex, 0, templateProfile.PreviewModeCount - 1);
            return templateProfile.GetPreviewMaterial(currentMaterialIndex);
        }

        return null;
    }

    private void Apply()
    {
        EnsureHelperComponents(true);

        var activeMaterial = ResolveActiveMaterial();
        if (activeMaterial == null)
        {
            return;
        }

        var specialMode = templateProfile != null && templateProfile.IsPreviewModeSpecial(currentMaterialIndex);
        interactionController.SetInteractionEnabled(!specialMode);

        if (specialMode)
        {
            ResetFullscreenQuadTransform();
            renderController.SetFullscreenQuadMode(true);
        }
        else
        {
            carrierMode = MapPreviewShapeToCarrierMode(previewShape);
            renderController.SetFullscreenQuadMode(false);
            renderController.SetCarrierMode(carrierMode);
        }

        renderController.ApplyPreviewMesh(templateProfile, currentMaterialIndex);

        var propertyBlock = renderController.PreparePropertyBlock(
            activeMaterial,
            transform,
            previewShape,
            OverrideMaterialShapeMode,
            interactionController.Pitch,
            interactionController.Yaw);

        BeforeApplyRenderProperties(propertyBlock, activeMaterial);
        renderController.ApplyPreparedPropertyBlock(activeMaterial, propertyBlock);
    }

    private void ResetFullscreenQuadTransform()
    {
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;
    }

    private static CarrierMode MapPreviewShapeToCarrierMode(PreviewShape shape)
    {
        return shape switch
        {
            PreviewShape.Sphere => CarrierMode.Sphere,
            PreviewShape.Box => CarrierMode.Cube,
            PreviewShape.Capsule => CarrierMode.Capsule,
            _ => CarrierMode.Cube
        };
    }
}
