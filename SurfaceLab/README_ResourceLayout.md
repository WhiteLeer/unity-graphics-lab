# SurfaceLab Resource Layout

## Core folders
- `Assets/SurfaceLab/SSS/Standard`
- `Assets/SurfaceLab/Crystal/Standard`

## Scope
- `SurfaceLab` now holds the graphics-side shared effect assets.
- SR-specific NPR assets now belong under `Assets/unity-sr-extraction-validation/NPR`.

## Notes
- `SurfaceLab` is the graphics-side effect domain formerly carried by `MaterialFX`.
- Internal shader names may still keep legacy `MaterialFX/...` labels for stability.
