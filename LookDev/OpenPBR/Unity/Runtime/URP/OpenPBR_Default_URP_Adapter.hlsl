#ifndef UNITY_GRAPHICS_LAB_OPENPBR_DEFAULT_URP_ADAPTER_INCLUDED
#define UNITY_GRAPHICS_LAB_OPENPBR_DEFAULT_URP_ADAPTER_INCLUDED

// Compact runtime surface path. It reuses OpenPBR's VNDF GGX, Smith masking,
// and IOR Fresnel implementations without instantiating the full path-tracing
// PreparedBsdf aggregate used by the reference integrator.
#include "../Port/AdobeOpenPBRBSDF/interop/openpbr_interop_slang.h"
#include "../Port/AdobeOpenPBRBSDF/openpbr_settings.h"
#include "../Port/AdobeOpenPBRBSDF/openpbr_constants.h"
#include "../Port/AdobeOpenPBRBSDF/impl/openpbr_vndf_microfacet_distribution.h"

struct UnityOpenPBRDefaultMaterial
{
    float3 baseColor;
    float baseWeight;
    float diffuseRoughness;
    float metalness;
    float specularWeight;
    float3 specularColor;
    float specularRoughness;
    float specularIor;
};

struct UnityOpenPBRDefaultResponse
{
    float3 diffuse;
    float3 specular;
};

UnityOpenPBRDefaultResponse UnityOpenPBR_EvaluateDefault(
    UnityOpenPBRDefaultMaterial material,
    float3 normal,
    float3 viewDirection,
    float3 lightDirection)
{
    UnityOpenPBRDefaultResponse response;
    response.diffuse = 0.0f;
    response.specular = 0.0f;

    const float nDotV = saturate(dot(normal, viewDirection));
    const float nDotL = saturate(dot(normal, lightDirection));
    if (nDotV <= 0.0f || nDotL <= 0.0f)
        return response;

    const float perceptualRoughness = saturate(material.specularRoughness);
    const float alpha = max(perceptualRoughness * perceptualRoughness, 0.0025f);
    const OpenPBR_Basis basis = openpbr_make_basis(normal);

    OpenPBR_AnisotropicGGXSmithVNDFMicrofacetDistribution distribution;
    distribution.alpha = vec2(alpha, alpha);
    distribution.basis_ff = basis;
    distribution.isotropic_alpha = alpha;

    const float3 halfVector = normalize(viewDirection + lightDirection);
    const float vDotH = saturate(dot(viewDirection, halfVector));
    const float D = openpbr_eval_ggx(distribution, halfVector, normal);
    const float G = openpbr_eval_smith_g2(distribution, viewDirection, lightDirection, nDotV, nDotL);

    const float dielectricFresnel = openpbr_fresnel(max(material.specularIor, 1.001f), vDotH);
    const float3 dielectricF = float3(dielectricFresnel, dielectricFresnel, dielectricFresnel);
    const float3 metalF = saturate(material.baseColor) * material.specularColor;
    const float3 F = lerp(dielectricF, metalF, saturate(material.metalness));
    const float specularDenominator = max(4.0f * nDotV * nDotL, 1.0e-4f);
    response.specular = D * G * F / specularDenominator * material.specularWeight;

    const float dielectricWeight = 1.0f - saturate(material.metalness);
    const float diffuseEnergy = 1.0f - saturate(dot(F, float3(0.3333333f, 0.3333333f, 0.3333333f)));
    const float diffuseRoughness = saturate(material.diffuseRoughness);
    const float roughDiffuse = lerp(1.0f, 0.85f, diffuseRoughness);
    response.diffuse = material.baseColor * material.baseWeight * dielectricWeight *
                       (diffuseEnergy * roughDiffuse * OpenPBR_RcpPi);
    return response;
}

#endif
