# unity-graphics-lab

In-project Git root for graphics rendering experiments and support assets.

## Layout

- `Shared/`
  - Editor utilities, URP pipeline assets, screenshots, shared materials, and common template scenes.
- `Grayscale/`
- `SSAO/`
- `ReflectionEffects/`
  - `SSPR/`
  - `SSR/`
- `Bloom/`
- `DoF/`
- `SSGI/`
- `MotionBlur/`
- `ColorGradingToneMapping/`
- `Crystal/`
- `SSS/`

## Current convention

- One effect folder.
- One local example scene per effect.
- Shared infrastructure stays under `Shared/`.
- The top level is organized by feature names instead of technical buckets like `Post` or `SurfaceLab`.
