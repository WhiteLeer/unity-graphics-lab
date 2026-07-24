#ifndef SURFACE_LAB_WATER_COMMON_INCLUDED
#define SURFACE_LAB_WATER_COMMON_INCLUDED

float2 WaterRotate2D(float2 value, float angle)
{
    float sineValue;
    float cosineValue;
    sincos(angle, sineValue, cosineValue);
    return float2(
        value.x * cosineValue - value.y * sineValue,
        value.x * sineValue + value.y * cosineValue);
}

uint WaterMurmurHash11(uint source)
{
    const uint multiplier = 0x5bd1e995u;
    uint hash = 1190494759u;
    source *= multiplier;
    source ^= source >> 24u;
    source *= multiplier;
    hash *= multiplier;
    hash ^= source;
    hash ^= hash >> 13u;
    hash *= multiplier;
    hash ^= hash >> 15u;
    return hash;
}

float WaterHash11(float source)
{
    uint hash = WaterMurmurHash11(asuint(source));
    return asfloat((hash & 0x007fffffu) | 0x3f800000u) - 1.0;
}

void WaterAccumulateWave(
    float2 position,
    float2 direction,
    float frequency,
    float amplitude,
    float speed,
    float time,
    inout float height,
    inout float2 gradient)
{
    float phase = dot(position, direction) * frequency + time * speed;
    float sineValue;
    float cosineValue;
    sincos(phase, sineValue, cosineValue);
    float crest = sineValue * 0.5 + 0.5;
    float shapedWave = crest * crest * 2.0 - 1.0;
    height += shapedWave * amplitude;
    gradient += direction * (2.0 * crest * cosineValue * amplitude * frequency);
}

void WaterEvaluateWaves(
    float2 position,
    float time,
    float frequency,
    float amplitude,
    float speed,
    float2 direction,
    out float height,
    out float2 gradient)
{
    float directionLength = max(length(direction), 1e-4);
    float2 baseDirection = direction / directionLength;
    height = 0.0;
    gradient = 0.0;

    // Keep the reference wave spectrum, but evaluate it at the carrier's vertices.
    // This preserves the 60-wave shape without adding a fragment loop to mesh modes.
    float frequencyScale = 1.0;
    float amplitudeScale = 1.0;
    float directionScale = 1.0;
    float totalWeight = 0.0;
    const float waveScale = 1.0812;
    [loop]
    for (int waveIndex = 0; waveIndex < 60; waveIndex++)
    {
        float randomValue = WaterHash11((float)waveIndex) * 2.0 - 1.0;
        float phase = 0.2 + randomValue * 0.75 * 3.14159265;
        float2 waveDirection = float2(sin(phase), cos(phase));
        waveDirection = WaterRotate2D(baseDirection, phase - 0.0316988);
        WaterAccumulateWave(
            position,
            waveDirection,
            frequency * frequencyScale,
            amplitude * amplitudeScale,
            speed * (1.0 + directionScale * 0.05),
            time,
            height,
            gradient);
        totalWeight += amplitudeScale;
        frequencyScale *= waveScale;
        amplitudeScale /= waveScale;
        directionScale *= waveScale;
    }
    height /= max(totalWeight, 1e-4);
    gradient /= max(totalWeight, 1e-4);
}

float3 WaterPerturbNormal(float3 positionWS, float3 normalWS, float time, float frequency, float strength)
{
    float3 direction0 = normalize(float3(0.76, 0.29, 0.58));
    float3 direction1 = normalize(float3(-0.43, 0.81, 0.39));
    float3 direction2 = normalize(float3(0.24, -0.67, 0.70));
    float phase0 = dot(positionWS, direction0) * frequency + time * 0.73;
    float phase1 = dot(positionWS, direction1) * (frequency * 1.73) + time * 1.07;
    float phase2 = dot(positionWS, direction2) * (frequency * 2.41) - time * 0.61;
    float3 detail = direction0 * (cos(phase0) * 0.52);
    detail += direction1 * (cos(phase1) * 0.31);
    detail += direction2 * (cos(phase2) * 0.17);
    detail -= normalWS * dot(detail, normalWS);
    return normalize(normalWS + detail * strength);
}

float WaterDielectricF0(float ior)
{
    float ratio = (1.0 - ior) / max(1.0 + ior, 1e-4);
    return ratio * ratio;
}

float WaterFresnel(float noV, float ior, float power)
{
    float f0 = WaterDielectricF0(max(ior, 1.001));
    return f0 + (1.0 - f0) * pow(1.0 - saturate(noV), max(power, 0.5));
}

float3 WaterTransmittance(float3 transmissionColor, float absorptionDensity, float thickness)
{
    float3 channelDistance = max(transmissionColor, 0.02);
    return exp(-max(thickness, 0.0) * max(absorptionDensity, 0.0) / channelDistance);
}

float2 WaterDistortedScreenUV(float4 screenPosition, float3 normalWS, float strength)
{
    float2 screenUV = screenPosition.xy / max(screenPosition.w, 1e-5);
    return saturate(screenUV + normalWS.xz * strength);
}

float WaterHasOpaqueBackground(float sceneDepth, float surfaceDepth)
{
    float behindSurface = step(surfaceDepth + 1e-3, sceneDepth);
    float beforeFarPlane = 1.0 - step(_ProjectionParams.z * 0.995, sceneDepth);
    return behindSurface * beforeFarPlane;
}

float WaterGGXSpecular(float3 normalWS, float3 viewDirectionWS, float3 lightDirectionWS, float smoothness, float ior)
{
    float3 halfDirection = SafeNormalize(viewDirectionWS + lightDirectionWS);
    float noV = max(saturate(dot(normalWS, viewDirectionWS)), 1e-4);
    float noL = max(saturate(dot(normalWS, lightDirectionWS)), 1e-4);
    float noH = saturate(dot(normalWS, halfDirection));
    float voH = saturate(dot(viewDirectionWS, halfDirection));
    float roughness = max(1.0 - smoothness, 0.08);
    float alpha = roughness * roughness;
    float alpha2 = alpha * alpha;
    float denominator = noH * noH * (alpha2 - 1.0) + 1.0;
    float distribution = alpha2 / max(3.14159265 * denominator * denominator, 1e-6);
    float geometryV = noV / lerp(noV, 1.0, alpha);
    float geometryL = noL / lerp(noL, 1.0, alpha);
    float fresnel = WaterDielectricF0(max(ior, 1.001));
    fresnel += (1.0 - fresnel) * pow(1.0 - voH, 5.0);
    return distribution * geometryV * geometryL * fresnel / max(4.0 * noV * noL, 1e-4);
}

float3 WaterSubsurface(
    float3 viewDirectionWS,
    float3 lowFrequencyNormalWS,
    float3 lightDirectionWS,
    float fresnel,
    float strength,
    float3 color)
{
    float viewAlignment = saturate(dot(viewDirectionWS, lightDirectionWS));
    float normalAlignment = saturate(dot(lowFrequencyNormalWS, lightDirectionWS));
    float response = pow(viewAlignment * normalAlignment, 2.0) * (1.0 - fresnel);
    return color * (response * strength);
}

#endif
