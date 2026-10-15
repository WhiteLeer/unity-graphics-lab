# OpenPBR for Unity LookDev

This directory contains the vendored OpenPBR specification, Adobe's reference
BSDF implementation, and the Unity integration boundary for this project.

## Contents

- `Reference/ASWF-OpenPBR`: OpenPBR specification material and MaterialX examples.
- `ThirdParty/AdobeOpenPBRBSDF`: Adobe's Apache-2.0 reference implementation,
  including the lobe code, volume helpers, and energy/LTC lookup data.
- `Unity/Runtime/Core`: public interop and core data-model entry points.
- `Unity/Runtime/Lobes`: lobe boundary; implementation stays vendored under
  `Runtime/Port`.
- `Unity/Runtime/Surface`: one-light surface preparation and evaluation.
- `Unity/Runtime/Volume`: homogeneous-volume coefficient and transmittance
  helpers.
- `Unity/Runtime/LUT`: reserved binding seam for energy/LTC lookup assets.
- `Unity/Runtime/URP`: the Unity-facing adapter boundary.
- `Unity/Runtime/Port`: mechanically adapted HLSL source kept behind the
  public headers above.
- `Unity/Tests`: compile probes only; these are not production materials.
- `Unity/Docs`: integration decisions and renderer boundary notes.

## Migration status

The upstream sources are preserved as read-only reference code. The Unity
runtime adapter is intentionally kept separate because the upstream API is
designed for a path-tracing integrator, while URP raster rendering supplies
light loops, shadows, environment lighting, refraction, and render targets.

The first integration target is the evaluable surface subset:

- diffuse and specular evaluation;
- metalness, IOR, roughness, and anisotropy;
- coat and thin-film lobes where the target shader enables them;
- transmission, absorption, and thickness inputs;
- subsurface parameters as a material response, with URP providing the actual
  thickness/SSS render passes;
- texture-backed energy compensation LUTs.

The Unity-facing layer deliberately excludes path-tracing integration. The
vendored port may still contain upstream `sample`/`pdf` and volume sampling
helpers because its evaluation code depends on the same internal data model;
they are not included by the URP adapter and are not runtime features. FFT
water simulation, screen-space refraction, SSR, caustics, and crystal interior
textures remain effect-specific extensions.

## Provenance

- ASWF OpenPBR: https://github.com/AcademySoftwareFoundation/OpenPBR
- Adobe OpenPBR BSDF: https://github.com/adobe/openpbr-bsdf
- Upstream snapshots are recorded in `Unity/Docs/OpenPBR_IntegrationNotes.md`.

Preserve the upstream `LICENSE` files when modifying or redistributing this
directory.
