#ifndef UNITY_GRAPHICS_LAB_OPENPBR_DEFAULT_INTEROP_INCLUDED
#define UNITY_GRAPHICS_LAB_OPENPBR_DEFAULT_INTEROP_INCLUDED

// The default material keeps the common opaque surface path small. Full
// transmission, dispersion, coat, and fuzz remain available to dedicated
// materials through OpenPBR_Interop.hlsl.
#define OPENPBR_LANGUAGE_TARGET_SLANG 1
#define OPENPBR_USE_CUSTOM_SATURATE 1

#define UNITY_OPENPBR_DEFAULT_FEATURE_EnableSheenAndCoat 0
#define UNITY_OPENPBR_DEFAULT_FEATURE_EnableDispersion 0
#define UNITY_OPENPBR_DEFAULT_FEATURE_EnableTranslucency 0
#define UNITY_OPENPBR_DEFAULT_FEATURE_EnableMetallic 1
#define OPENPBR_GET_SPECIALIZATION_CONSTANT(name) UNITY_OPENPBR_DEFAULT_FEATURE_##name

#include "../Port/AdobeOpenPBRBSDF/openpbr.h"

#endif
