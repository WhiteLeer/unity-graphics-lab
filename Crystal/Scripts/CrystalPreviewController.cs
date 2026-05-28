using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public sealed class CrystalPreviewController : VolumeMaterialPreviewController
{
    protected override PreviewShape DefaultPreviewShape => PreviewShape.Box;
}
