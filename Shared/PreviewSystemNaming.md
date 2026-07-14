# Preview System Naming

## Shared layer
- `SceneTemplate`: Unity scene template asset that defines the common scene skeleton.
- `PreviewCarrierTemplate`: shared prefab that contains the preview carrier meshes and the fullscreen quad carrier.
- `VolumePreviewSceneProfile`: per-effect scene profile that defines preview modes, background color, and interaction defaults.

## Folder rules
- `Editor/`: effect-specific editor code, inspectors, and asmdefs only.
- `Pipeline/`: effect-specific URP pipeline assets only.
- `Resources/`: runtime-loaded materials and textures, flat at this level, no subfolders.
- `Scenes/`: scene files only.
- `Scripts/`: runtime scripts only.
- `Standard/`: shader files and shared HLSL only.
- Shared systems that are reused by multiple effects stay under `Shared/`.

## Effect layer
- `M_<Effect>_Preview`: preview/default material for a mode that renders the shared preview presentation.
- `M_<Effect>_Object`: object-mode material that renders on the actual mesh carrier.
- `M_<Effect>_SimpleJade`: simplified JadeVolume mode material.
- Shader files use `Crystal_*.shader`, `JadeVolume_*.shader`, and shared HLSL helpers use descriptive file names.

## Script layer
- Shared controllers stay under `Shared/` and own one responsibility each.
- Effect-specific controllers keep only effect-specific policy; they should not re-own preview switching, camera motion, or carrier activation.
- Layout-only scripts should be named as layout scripts, not as preview controllers.
