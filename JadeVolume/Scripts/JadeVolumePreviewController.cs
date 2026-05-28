using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public sealed class JadeVolumePreviewController : VolumeMaterialPreviewController
{
    // Jade-specific policy:
    // - This class intentionally uses the generic preview controller behavior as-is.
    // - Layout/placement for the jade example belongs to JadeVolumeController, not here.
    // - Mode switching and zoom persistence belong to ReferencePreviewToggleController, not here.
    // Keep Jade preview behavior changes inside the base controller contract unless jade truly needs a unique policy.
}
