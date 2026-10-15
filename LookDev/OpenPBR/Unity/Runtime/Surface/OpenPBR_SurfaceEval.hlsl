#ifndef UNITY_GRAPHICS_LAB_OPENPBR_SURFACE_EVAL_INCLUDED
#define UNITY_GRAPHICS_LAB_OPENPBR_SURFACE_EVAL_INCLUDED

#include "../Core/OpenPBR_Core.hlsl"
#include "../Lobes/OpenPBR_Lobes.hlsl"

// Unity-facing surface boundary. Sampling, light accumulation, shadows and
// render-pipeline integration remain the responsibility of the caller.
OpenPBR_PreparedBsdf UnityOpenPBR_PrepareForLight(
    OpenPBR_ResolvedInputs inputs,
    float3 pathThroughput,
    float3 rgbWavelengthsNm,
    float exteriorIor,
    float3 viewDirection)
{
    return openpbr_prepare(
        inputs,
        pathThroughput,
        rgbWavelengthsNm,
        exteriorIor,
        viewDirection);
}

OpenPBR_DiffuseSpecular UnityOpenPBR_EvaluateLight(
    OpenPBR_PreparedBsdf prepared,
    float3 lightDirection)
{
    return openpbr_eval(prepared, lightDirection);
}

#endif
