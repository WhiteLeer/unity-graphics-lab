#ifndef UNITY_GRAPHICS_LAB_OPENPBR_URP_ADAPTER_INCLUDED
#define UNITY_GRAPHICS_LAB_OPENPBR_URP_ADAPTER_INCLUDED

#define UNITY_OPENPBR_PI 3.14159265359f
#define UNITY_OPENPBR_RCP_PI 0.31830988618f

struct UnityOpenPBRMaterial
{
    float baseWeight;
    float3 baseColor;
    float baseDiffuseRoughness;
    float baseMetalness;

    float subsurfaceWeight;
    float3 subsurfaceColor;
    float subsurfaceRadius;
    float3 subsurfaceRadiusScale;
    float subsurfaceScatterAnisotropy;

    float specularWeight;
    float3 specularColor;
    float specularRoughness;
    float specularRoughnessAnisotropy;
    float specularIor;
    float specularAnisotropyRotation;

    float coatWeight;
    float3 coatColor;
    float coatRoughness;
    float coatRoughnessAnisotropy;
    float coatIor;
    float coatDarkening;
    float coatAnisotropyRotation;

    float fuzzWeight;
    float3 fuzzColor;
    float fuzzRoughness;

    float transmissionWeight;
    float3 transmissionColor;
    float transmissionDepth;
    float3 transmissionScatter;
    float transmissionScatterAnisotropy;
    float transmissionDispersionScale;
    float transmissionDispersionAbbeNumber;

    float thinFilmWeight;
    float thinFilmThickness;
    float thinFilmIor;

    float3 emissionColor;
    float emissionLuminance;
    float geometryOpacity;
    float geometryThinWalled;
    float exteriorIor;
};

struct UnityOpenPBRPreparedMaterial
{
    UnityOpenPBRMaterial material;
    float3 normal;
    float3 tangent;
    float3 bitangent;
    float3 viewDirection;
};

struct UnityOpenPBRLightResponse
{
    float3 diffuse;
    float3 specular;
    float3 transmission;
};

float UnityOpenPBR_IorF0(float ior)
{
    const float ratio = (ior - 1.0f) / (ior + 1.0f);
    return ratio * ratio;
}

float3 UnityOpenPBR_FresnelSchlick(float3 f0, float cosine)
{
    const float edge = pow(1.0f - saturate(cosine), 5.0f);
    return f0 + (1.0f - f0) * edge;
}

float UnityOpenPBR_SmithG1(float nDotV, float roughness)
{
    const float k = pow(roughness + 1.0f, 2.0f) * 0.125f;
    return nDotV / max(nDotV * (1.0f - k) + k, 1.0e-4f);
}

float UnityOpenPBR_AnisotropicGgxD(
    float3 halfVector,
    UnityOpenPBRPreparedMaterial prepared,
    float roughness,
    float anisotropy,
    float rotationDegrees)
{
    const float rotation = radians(rotationDegrees);
    const float2 rotationCosSin = float2(cos(rotation), sin(rotation));
    const float3 rotatedTangent = prepared.tangent * rotationCosSin.x + prepared.bitangent * rotationCosSin.y;
    const float3 rotatedBitangent = prepared.bitangent * rotationCosSin.x - prepared.tangent * rotationCosSin.y;
    const float3 hLocal = float3(
        dot(halfVector, rotatedTangent),
        dot(halfVector, rotatedBitangent),
        dot(halfVector, prepared.normal));
    const float ax = max(roughness * roughness * (1.0f + anisotropy), 0.0025f);
    const float ay = max(roughness * roughness * (1.0f - anisotropy), 0.0025f);
    const float z = max(hLocal.z, 1.0e-4f);
    const float ellipse = hLocal.x * hLocal.x / ax + hLocal.y * hLocal.y / ay + z * z;
    return 1.0f / max(UNITY_OPENPBR_PI * sqrt(ax * ay) * ellipse * ellipse, 1.0e-4f);
}

float3 UnityOpenPBR_ApplyThinFilm(
    float3 fresnel,
    UnityOpenPBRMaterial material,
    float cosine)
{
    if (material.thinFilmWeight <= 0.0f)
        return fresnel;

    const float phase = 6.28318530718f * material.thinFilmThickness * material.thinFilmIor *
                        (1.0f - saturate(cosine));
    const float3 phaseOffsets = float3(0.0f, 2.09439510239f, 4.18879020478f);
    const float3 interference = 0.5f + 0.5f * cos(phase + phaseOffsets);
    const float3 filmFresnel = saturate(fresnel + interference * 0.5f);
    return lerp(fresnel, filmFresnel, saturate(material.thinFilmWeight));
}

float3 UnityOpenPBR_EvaluateReflection(
    UnityOpenPBRPreparedMaterial prepared,
    float3 lightDirection,
    float3 f0,
    float roughness,
    float anisotropy,
    float rotationDegrees,
    float weight)
{
    const float nDotV = saturate(dot(prepared.normal, prepared.viewDirection));
    const float nDotL = saturate(dot(prepared.normal, lightDirection));
    if (nDotV <= 0.0f || nDotL <= 0.0f || weight <= 0.0f)
        return 0.0f;

    const float3 halfVector = SafeNormalize(prepared.viewDirection + lightDirection);
    const float vDotH = saturate(dot(prepared.viewDirection, halfVector));
    const float d = UnityOpenPBR_AnisotropicGgxD(
        halfVector, prepared, roughness, anisotropy, rotationDegrees);
    const float g = UnityOpenPBR_SmithG1(nDotV, roughness) * UnityOpenPBR_SmithG1(nDotL, roughness);
    const float3 f = UnityOpenPBR_FresnelSchlick(f0, vDotH);
    return d * g * f / max(4.0f * nDotV, 1.0e-4f) * nDotL * weight;
}

UnityOpenPBRPreparedMaterial UnityOpenPBR_PrepareUrpMaterial(
    UnityOpenPBRMaterial material,
    float3 normal,
    float3 tangent,
    float tangentHandedness,
    float3 viewDirection)
{
    UnityOpenPBRPreparedMaterial prepared;
    prepared.material = material;
    prepared.normal = SafeNormalize(normal);
    prepared.tangent = SafeNormalize(tangent - dot(tangent, prepared.normal) * prepared.normal);
    prepared.bitangent = SafeNormalize(cross(prepared.normal, prepared.tangent) * tangentHandedness);
    prepared.viewDirection = SafeNormalize(viewDirection);
    return prepared;
}

UnityOpenPBRLightResponse UnityOpenPBR_EvaluatePreparedUrpLight(
    UnityOpenPBRPreparedMaterial prepared,
    float3 lightDirection)
{
    UnityOpenPBRLightResponse response;
    response.diffuse = 0.0f;
    response.specular = 0.0f;
    response.transmission = 0.0f;

    const UnityOpenPBRMaterial material = prepared.material;
    const float nDotLRaw = dot(prepared.normal, lightDirection);
    const float nDotL = saturate(nDotLRaw);
    const float nDotV = saturate(dot(prepared.normal, prepared.viewDirection));

    const float dielectricF0 = UnityOpenPBR_IorF0(max(material.specularIor / material.exteriorIor, 1.001f));
    const float3 dielectricF0Color = dielectricF0 * saturate(material.specularColor);
    const float3 metalF0 = saturate(material.baseColor) * saturate(material.specularColor);
    const float3 baseF0 = lerp(dielectricF0Color, metalF0, saturate(material.baseMetalness));

    float3 baseF0WithFilm = UnityOpenPBR_ApplyThinFilm(
        baseF0, material, max(nDotV, nDotL));
    const float3 baseSpecular = UnityOpenPBR_EvaluateReflection(
        prepared,
        lightDirection,
        baseF0WithFilm,
        max(material.specularRoughness, 0.0025f),
        material.specularRoughnessAnisotropy,
        material.specularAnisotropyRotation,
        material.specularWeight);

    const float baseEnergy = 1.0f - dot(baseF0WithFilm, float3(0.3333333f, 0.3333333f, 0.3333333f));
    const float diffuseRoughness = saturate(material.baseDiffuseRoughness);
    const float roughDiffuse = lerp(1.0f, 0.85f, diffuseRoughness);
    const float3 baseDiffuse = material.baseColor * material.baseWeight *
                               (1.0f - saturate(material.baseMetalness)) *
                               max(baseEnergy, 0.0f) * roughDiffuse * UNITY_OPENPBR_RCP_PI * nDotL;

    const float scatterRadius = max(material.subsurfaceRadius, 0.0001f);
    const float wrappedLight = saturate((nDotLRaw + scatterRadius * 0.5f) / (1.0f + scatterRadius * 0.5f));
    const float backScatter = saturate(-nDotLRaw) * (0.5f + 0.5f * material.subsurfaceScatterAnisotropy);
    const float3 subsurfaceTint = material.subsurfaceColor * saturate(material.subsurfaceRadiusScale);
    const float3 subsurface = material.baseColor * subsurfaceTint *
                              (wrappedLight + backScatter) * UNITY_OPENPBR_RCP_PI;
    response.diffuse = lerp(baseDiffuse, subsurface, saturate(material.subsurfaceWeight));
    response.specular = baseSpecular;

    const float coatF0Scalar = UnityOpenPBR_IorF0(max(material.coatIor / material.exteriorIor, 1.001f));
    const float3 coatF0 = coatF0Scalar * saturate(material.coatColor);
    const float3 coatSpecular = UnityOpenPBR_EvaluateReflection(
        prepared,
        lightDirection,
        coatF0,
        max(material.coatRoughness, 0.0025f),
        material.coatRoughnessAnisotropy,
        material.coatAnisotropyRotation,
        material.coatWeight);
    const float coatReflectance = dot(coatF0, float3(0.3333333f, 0.3333333f, 0.3333333f));
    const float coatAttenuation = 1.0f - saturate(material.coatWeight * coatReflectance * material.coatDarkening);
    response.diffuse *= coatAttenuation;
    response.specular = response.specular * coatAttenuation + coatSpecular;

    const float fuzzEdge = pow(1.0f - max(nDotV, 0.0f), lerp(1.0f, 6.0f, saturate(material.fuzzRoughness)));
    response.diffuse += material.fuzzColor * material.fuzzWeight * fuzzEdge * nDotL * UNITY_OPENPBR_RCP_PI;

    if (material.transmissionWeight > 0.0f)
    {
        const float transmissionSide = material.geometryThinWalled > 0.5f ? 1.0f : saturate(-nDotLRaw);
        const float transmissionFresnel = saturate(1.0f - dielectricF0);
        const float absorption = exp(-material.transmissionScatter * max(material.transmissionDepth, 0.0f));
        response.transmission = material.transmissionColor * absorption * material.transmissionWeight *
                                transmissionSide * transmissionFresnel * UNITY_OPENPBR_RCP_PI;
    }

    return response;
}

float UnityOpenPBR_GetDispersionIor(UnityOpenPBRMaterial material, float wavelengthNm)
{
    const float normalizedWavelength = (550.0f - wavelengthNm) / 100.0f;
    return max(material.specularIor + normalizedWavelength *
               material.transmissionDispersionScale / max(material.transmissionDispersionAbbeNumber, 1.0f), 1.001f);
}

float3 UnityOpenPBR_TransmissionAttenuation(
    UnityOpenPBRPreparedMaterial prepared,
    float distance)
{
    return exp(-prepared.material.transmissionScatter * max(distance, 0.0f));
}

float3 UnityOpenPBR_GetEmission(UnityOpenPBRPreparedMaterial prepared)
{
    return prepared.material.emissionColor * prepared.material.emissionLuminance;
}

#endif
