#ifndef JADEVOLUME_MESH_SURFACE_COMMON_INCLUDED
#define JADEVOLUME_MESH_SURFACE_COMMON_INCLUDED

float JadeVolumeSampleNoise(float3 positionOS)
{
    float3 uvw = saturate(positionOS * _NoiseFrequency + 0.5);
    return SAMPLE_TEXTURE3D(_DensityTex, sampler_DensityTex, uvw).r;
}

float JadeVolumeG1V(float dnv, float k)
{
    return 1.0 / (dnv * (1.0 - k) + k);
}

float JadeVolumeGGX(float3 n, float3 v, float3 l, float rough, float f0)
{
    float alpha = rough * rough;
    float3 h = normalize(v + l);
    float dnl = saturate(dot(n, l));
    float dnv = saturate(dot(n, v));
    float dnh = saturate(dot(n, h));
    float dlh = saturate(dot(l, h));
    float asqr = alpha * alpha;
    float den = dnh * dnh * (asqr - 1.0) + 1.0;
    float d = asqr / (PI * den * den);
    float f = f0 + (1.0 - f0) * pow(1.0 - dlh, 5.0);
    float vis = JadeVolumeG1V(dnl, alpha) * JadeVolumeG1V(dnv, alpha);
    return dnl * d * f * vis;
}

float JadeVolumeSurfaceThickness(float3 positionOS, float3 normalOS, float3 viewDirOS, float sampleCount, float sampleDepth)
{
    float3 inwardDirOS = normalize(-viewDirOS + normalOS * 0.35);
    if (dot(inwardDirOS, inwardDirOS) < 1e-4)
    {
        inwardDirOS = -normalOS;
    }

    int steps = max(1, (int)round(sampleCount));
    float depth = max(sampleDepth, 0.001);
    float thickness = 0.0;

    [loop]
    for (int i = 0; i < 64; i++)
    {
        if (i >= steps)
        {
            break;
        }

        float t = depth * ((float)i + 0.5) / max((float)steps, 1.0);
        float density = JadeVolumeSampleNoise(positionOS + inwardDirOS * t);
        thickness += saturate(density * 1.3 - 0.18);
    }

    return saturate(thickness / max((float)steps, 1.0));
}

float JadeVolumeTransmission(float3 positionOS, float3 normalOS, float3 viewDirOS, float scatterDistance, float scatterStep, float scatterStrength, float scatterBoost, float scatterCurve, float scatterIor)
{
    float3 refractedDirOS = refract(-viewDirOS, normalOS, 1.0 / max(scatterIor, 1.001));
    if (dot(refractedDirOS, refractedDirOS) < 1e-4)
    {
        refractedDirOS = -normalOS;
    }

    float stepLen = max(scatterStep, 0.001);
    int steps = max(1, (int)ceil(scatterDistance / stepLen));
    float densitySum = 0.0;

    [loop]
    for (int i = 0; i < 64; i++)
    {
        if (i >= steps)
        {
            break;
        }

        float t = stepLen * ((float)i + 1.0);
        float density = JadeVolumeSampleNoise(positionOS + refractedDirOS * t) - 0.5;
        densitySum += saturate(density + 0.5) * stepLen;
    }

    float transmission = (1.0 - exp(-densitySum * max(scatterStrength, 1e-3))) * scatterBoost;
    return pow(saturate(transmission), max(scatterCurve, 1e-3));
}

#endif
