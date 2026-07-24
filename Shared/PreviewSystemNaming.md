# Preview System Naming

## Shared layer
- `LookDev`: shared scene template asset that defines the common lookdev skeleton.
- `PreviewCarrierTemplate`: shared prefab that contains the preview carrier meshes and the fullscreen quad carrier.
- `VolumePreviewSceneProfile`: per-effect scene profile that defines preview modes, background color, and interaction defaults.
- `Shared/Profiles/VolumePreviewSceneProfile.asset`: blank defaults used only by the LookDev source scene; it must not contain Crystal or JadeVolume materials.
- `CrystalVolumePreviewSceneProfile`, `JadeVolumePreviewSceneProfile`, and `WaterPreviewSceneProfile`: effect-owned mode configurations used by the generated example scenes.
- `LookDevCalibrationVolumeProfile`: shared neutral color-management profile. It enables URP Tonemapping, White Balance, and Exposure with explicit neutral defaults.
- `LookDevCalibration`: shared calibration prefab containing the fixed-screen UI color chart. The preview `MatBall` is supplied by the configured preview mode and is not duplicated here.

## Update rules
- The Unity Scene Template is only the creation source. It does not synchronize scenes that were created earlier.
- Shared hierarchy and carrier changes belong in `PreviewCarrierTemplate` or a shared LookDev prefab.
- Shared behavior belongs in `Shared/` scripts. Effect-specific differences belong in the effect Profile.
- Existing scenes are updated by `Tools/Unity Graphics Lab/预览系统/迁移所有已配置场景`.
- Run `Tools/Unity Graphics Lab/预览系统/校验所有已配置场景` after scene or Profile changes.
- A mode owns one material, one preview prefab, and its fullscreen-quad flag. Runtime interaction must not instantiate a second prefab or select a scene-local mesh source.
- All LookDev/example scenes use the same calibration Volume Profile and calibration prefab. Effect-specific lighting remains in the effect scene.
- The color chart is a fixed sRGB texture displayed by a minimal URP Unlit material. The chart is an external color reference, so its values are not duplicated in effect shaders.

## Folder rules
- `Editor/`: effect-specific editor code, inspectors, and asmdefs only.
- `Pipeline/`: effect-specific URP pipeline assets only.
- `Resources/`: runtime-loaded materials and textures, flat at this level, no subfolders.
- `Scenes/`: scene files only.
- `Scripts/`: runtime scripts only.
- `Standard/`: shader files and shared HLSL only.
- `Materials/`: shared calibration and presentation materials only.
- `Textures/`: shared calibration textures only.
- `Pipeline/LookDev/`: shared LookDev Volume Profiles and related color-management assets.
- Shared systems that are reused by multiple effects stay under `Shared/`.

## Effect layer
- `M_<Effect>_Preview`: preview/default material for a mode that renders the shared preview presentation.
- `M_<Effect>_Object`: object-mode material that renders on the actual mesh carrier.
- `M_<Effect>_SimpleJade`: simplified JadeVolume mode material.
- Water uses `M_Water_0` for the copied Shadertoy ocean, `M_Water_1` for the real plane surface, and `M_Water_2` for the closed-mesh water volume.
- Shader files use `Crystal_*.shader`, `JadeVolume_*.shader`, `Water_*.shader`, and shared HLSL helpers use descriptive file names.

## Script layer
- Shared controllers stay under `Shared/` and own one responsibility each.
- Effect-specific controllers keep only effect-specific policy; they should not re-own preview switching, camera motion, or carrier activation.
- Layout-only scripts should be named as layout scripts, not as preview controllers.
