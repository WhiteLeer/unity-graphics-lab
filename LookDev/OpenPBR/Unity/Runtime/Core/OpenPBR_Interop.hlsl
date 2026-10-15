#ifndef UNITY_GRAPHICS_LAB_OPENPBR_INTEROP_INCLUDED
#define UNITY_GRAPHICS_LAB_OPENPBR_INTEROP_INCLUDED

// Adobe's Slang interop is HLSL-shaped, so it is the closest upstream backend
// for Unity's HLSL compiler. This file is only an adapter boundary; effect
// shaders should include this wrapper rather than the vendored headers.
#define OPENPBR_LANGUAGE_TARGET_SLANG 1
#define OPENPBR_USE_CUSTOM_SATURATE 1
#define OPENPBR_GET_SPECIALIZATION_CONSTANT(name) 1

#include "../Port/AdobeOpenPBRBSDF/openpbr.h"

#endif
