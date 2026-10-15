# Unity Integration Notes

## What can be reused

The following parts are suitable for a Unity HLSL port or controlled adapter:

- resolved material input layout;
- tangent/normal basis construction;
- diffuse, microfacet, coat, fuzz, transmission, and thin-film lobe math;
- diffuse/specular split responses;
- energy-compensation and LTC lookup data;
- Beer-Lambert transmittance and phase-function formulas;
- compile-time feature switches.

## What remains renderer-specific

The upstream implementation does not own Unity's render loop. These parts
must be integrated with URP rather than copied into a material function:

- main-light and additional-light iteration;
- shadow attenuation and light cookies;
- reflection probes, sky lighting, and screen-space reflections;
- camera opaque texture and screen-space refraction;
- thickness/backface render passes;
- screen-space or texture-space SSS filtering;
- volume ray marching and random-walk event integration;
- ray-tracing acceleration structures and dispatch.

The `Unity/Runtime/Port/AdobeOpenPBRBSDF` copy contains only mechanical HLSL
syntax adaptation needed by Unity 2022.3, while
`ThirdParty/AdobeOpenPBRBSDF` remains the untouched upstream snapshot. The
Unity-facing runtime is split into `Core`, `Lobes`, `Surface`, `Volume`, `LUT`,
and `URP` headers. It is not yet the final production URP material.

## Planned adapter boundary

The current Unity-facing API receives resolved inputs and returns a
diffuse/specular response for one light direction. The URP wrapper owns light
loops and shadow data, while Crystal and Water wrappers provide their custom
material inputs.

```text
texture/material properties
        -> OpenPBR resolved inputs
        -> OpenPBR surface evaluation
        -> URP light loop and shadows
        -> effect-specific reflection/refraction/volume passes
```

## Removed from the Unity runtime surface

The Unity adapter does not include path-tracing entry points, event-distance
sampling, phase-function sampling, random-walk integration, ray-generation,
closest-hit, any-hit, payload, dispatch, or acceleration-structure code. The
upstream snapshot and the internal port retain source-level dependencies where
needed for traceability and future backends, but none of these APIs are
reachable through the URP adapter.
