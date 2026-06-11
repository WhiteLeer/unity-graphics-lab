using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public sealed class CrystalPreviewController : VolumeMaterialPreviewController
{
    // Crystal-specific policy:
    // - Default preview shape is Box.
    // - Switching preview material mode may seed the runtime shape from the material's _ShapeMode.
    // - After that seed, the runtime shape is still owned by VolumeMaterialPreviewController.
    // Do not add extra Crystal-side writes to shape/zoom/mode outside the base controller APIs.
    protected override PreviewShape DefaultPreviewShape => PreviewShape.Box;
    protected override bool OverrideMaterialShapeMode => true;
    protected override bool SyncShapeFromMaterialOnMaterialChange => true;
}
