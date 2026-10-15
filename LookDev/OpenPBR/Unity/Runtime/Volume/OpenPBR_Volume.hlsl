#ifndef UNITY_GRAPHICS_LAB_OPENPBR_VOLUME_INCLUDED
#define UNITY_GRAPHICS_LAB_OPENPBR_VOLUME_INCLUDED

#include "../Core/OpenPBR_Core.hlsl"

float3 UnityOpenPBR_CalculateScatteringCoefficient(OpenPBR_HomogeneousVolume volume)
{
    return openpbr_calculate_scattering_coefficient(volume);
}

float3 UnityOpenPBR_TransmittanceAtDistance(
    OpenPBR_HomogeneousVolume volume,
    float distance)
{
    return openpbr_calculate_transmittance_at_distance(volume, distance);
}

float3 UnityOpenPBR_TransmittanceAtDistance(
    float3 extinctionCoefficient,
    float distance)
{
    return openpbr_calculate_transmittance_at_distance(
        extinctionCoefficient,
        distance);
}

#endif
