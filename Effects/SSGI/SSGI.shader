Shader "Hidden/Func6/SSGI"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

    TEXTURE2D(_BaseMap);
    SAMPLER(sampler_BaseMap);

    TEXTURE2D(_SSGIHistoryTex);
    SAMPLER(sampler_SSGIHistoryTex);

    TEXTURE2D(_SSGIIndirectTex);
    SAMPLER(sampler_SSGIIndirectTex);

    TEXTURE2D(_SSGISourceTex);
    SAMPLER(sampler_SSGISourceTex);
    TEXTURE2D(_MotionVectorTexture);
    SAMPLER(sampler_MotionVectorTexture);

    float4 _SSGIParams;          // x: intensity, y: rayLength, z: thickness, w: rayBias
    float4 _SSGIParams2;         // x: sampleCount, y: stepCount, z: distanceFalloff
    float4 _SSGIParamsDenoise;   // x: enabled, y: radius, z: depthSigma, w: normalPower
    float4 _SSGIParamsTemporal;  // x: enabled, y: response, z: clampScale
    float4 _SSGIParamsTemporalReject; // x: depthReject, y: normalRejectCos, z: motionReject
    float4 _SSGIDenoiseDir;      // x,y: denoise direction (1,0) or (0,1)
    float4 _SSGIScreenSize;      // x: width, y: height, z: 1/width, w: 1/height
    float4 _SSGIFullScreenSize;  // x: width, y: height, z: 1/width, w: 1/height
    float4x4 _SSGIViewProj;
    float4x4 _SSGIInvViewProj;
    float4x4 _SSGIPrevViewProj;
    int _SSGIHistoryValid;
    float _SSGIFrameIndex;

    #define SSGI_INTENSITY _SSGIParams.x
    #define SSGI_RAY_LENGTH _SSGIParams.y
    #define SSGI_THICKNESS _SSGIParams.z
    #define SSGI_RAY_BIAS _SSGIParams.w
    #define SSGI_SAMPLE_COUNT int(_SSGIParams2.x)
    #define SSGI_STEP_COUNT int(_SSGIParams2.y)
    #define SSGI_DISTANCE_FALLOFF _SSGIParams2.z

    #define SSGI_DENOISE_RADIUS int(_SSGIParamsDenoise.y)
    #define SSGI_DENOISE_DEPTH_SIGMA _SSGIParamsDenoise.z
    #define SSGI_DENOISE_NORMAL_POWER _SSGIParamsDenoise.w

    #define SSGI_TEMPORAL_RESPONSE _SSGIParamsTemporal.y
    #define SSGI_TEMPORAL_CLAMP_SCALE _SSGIParamsTemporal.z
    #define SSGI_INDIRECT_CLAMP _SSGIParamsTemporal.w
    #define SSGI_TEMPORAL_MOTION_REJECT _SSGIParamsTemporalReject.z

    struct Attributes
    {
        float4 positionOS : POSITION;
        float2 uv : TEXCOORD0;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        output.uv = input.uv;
        return output;
    }

    float Hash12(float2 p)
    {
        float3 p3 = frac(float3(p.xyx) * 0.1031);
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.x + p3.y) * p3.z);
    }

    float2 Hammersley(uint i, uint n)
    {
        uint bits = (i << 16u) | (i >> 16u);
        bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xAAAAAAAAu) >> 1u);
        bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xCCCCCCCCu) >> 2u);
        bits = ((bits & 0x0F0F0F0Fu) << 4u) | ((bits & 0xF0F0F0F0u) >> 4u);
        bits = ((bits & 0x00FF00FFu) << 8u) | ((bits & 0xFF00FF00u) >> 8u);
        return float2(float(i) / max(float(n), 1.0), float(bits) * 2.3283064365386963e-10);
    }

    float3 SampleHemisphereCosine(float2 xi)
    {
        float phi = TWO_PI * xi.x;
        float cosTheta = sqrt(saturate(1.0 - xi.y));
        float sinTheta = sqrt(saturate(xi.y));
        return float3(cos(phi) * sinTheta, sin(phi) * sinTheta, cosTheta);
    }

    float3x3 BuildBasis(float3 n)
    {
        float3 up = abs(n.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
        float3 t = normalize(cross(up, n));
        float3 b = cross(n, t);
        return float3x3(t, b, n);
    }

    float2 ClipToScreenUV(float4 clipPos)
    {
        float2 uv = clipPos.xy / clipPos.w * 0.5 + 0.5;
        if (_ProjectionParams.x < 0.0)
            uv.y = 1.0 - uv.y;
        return uv;
    }

    bool TraceScreenRay(float3 originWS, float3 dirWS, out float2 hitUV, out float hitDistance)
    {
        hitUV = 0.0;
        hitDistance = 0.0;

        float stepLen = SSGI_RAY_LENGTH / max(1, SSGI_STEP_COUNT);
        [loop]
        for (int i = 1; i <= max(1, SSGI_STEP_COUNT); i++)
        {
            float travel = stepLen * i;
            float3 sampleWS = originWS + dirWS * travel;
            float4 clipPos = mul(_SSGIViewProj, float4(sampleWS, 1.0));
            if (clipPos.w <= 0.00001)
                break;

            float2 uv = ClipToScreenUV(clipPos);
            if (uv.x <= 0.0 || uv.x >= 1.0 || uv.y <= 0.0 || uv.y >= 1.0)
                break;

            float rawDepth = SampleSceneDepth(uv);
            if (rawDepth < 0.00001)
                continue;

            float3 sceneWS = ComputeWorldSpacePosition(uv, rawDepth, _SSGIInvViewProj);
            float rayDepthVS = -mul(UNITY_MATRIX_V, float4(sampleWS, 1.0)).z;
            float sceneDepthVS = -mul(UNITY_MATRIX_V, float4(sceneWS, 1.0)).z;
            float depthDiff = sceneDepthVS - rayDepthVS;
            float thickness = max(SSGI_THICKNESS, 0.001) * (1.0 + sceneDepthVS * 0.02);

            if (depthDiff <= 0.0 && depthDiff >= -thickness)
            {
                hitUV = uv;
                hitDistance = travel;
                return true;
            }
        }

        return false;
    }

    float3 ComputeRawIndirect(float2 uv)
    {
        float rawDepth = SampleSceneDepth(uv);
        if (rawDepth < 0.00001)
            return 0.0;

        float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, _SSGIInvViewProj);
        float3 normalWS = normalize(SampleSceneNormals(uv));
        if (dot(normalWS, normalWS) < 0.25)
            return 0.0;

        float3x3 basis = BuildBasis(normalWS);
        float frameSeed = frac(_SSGIFrameIndex * 0.61803398875);
        float randomAngle = Hash12(uv * _SSGIScreenSize.xy + float2(frameSeed, frameSeed * 1.37)) * TWO_PI;
        float sinA = sin(randomAngle);
        float cosA = cos(randomAngle);
        float2x2 rot = float2x2(cosA, -sinA, sinA, cosA);
        float2 cpOffset = float2(
            frac(0.754877666f * (_SSGIFrameIndex + 1.0f)),
            frac(0.569840291f * (_SSGIFrameIndex + 1.0f))
        );

        float3 accumGI = 0.0;
        float totalWeight = 0.0;
        int samples = max(1, SSGI_SAMPLE_COUNT);

        [loop]
        for (int s = 0; s < samples; s++)
        {
            float2 xi = Hammersley((uint)s, (uint)samples);
            xi = frac(xi + cpOffset);
            xi = mul(rot, xi - 0.5) + 0.5;
            xi = saturate(xi);

            float3 localDir = SampleHemisphereCosine(xi);
            float3 rayDirWS = normalize(mul(basis, localDir));
            float ndotL = saturate(dot(normalWS, rayDirWS));
            if (ndotL <= 0.0001)
                continue;

            float2 hitUV;
            float hitDistance;
            float3 originWS = worldPos + normalWS * max(SSGI_RAY_BIAS, 0.0005);
            bool hit = TraceScreenRay(originWS, rayDirWS, hitUV, hitDistance);
            if (!hit)
                continue;

            float3 hitColor = SAMPLE_TEXTURE2D(_SSGISourceTex, sampler_SSGISourceTex, hitUV).rgb;
            float distAttn = 1.0 / (1.0 + max(0.0, SSGI_DISTANCE_FALLOFF) * hitDistance * hitDistance);
            float w = ndotL * distAttn;

            accumGI += hitColor * w;
            totalWeight += w;
        }

        float3 indirect = (totalWeight > 1e-4) ? (accumGI / totalWeight) : 0.0;
        float maxComponent = max(indirect.r, max(indirect.g, indirect.b));
        float clampValue = max(0.05, SSGI_INDIRECT_CLAMP);
        if (maxComponent > clampValue)
            indirect *= clampValue / maxComponent;
        return indirect;
    }

    float3 DenoiseBilateral(float2 uv)
    {
        float2 texel = _SSGIScreenSize.zw;
        float centerDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
        float3 centerNormal = normalize(SampleSceneNormals(uv));
        float3 center = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;

        float3 sum = 0.0;
        float wsum = 0.0;
        int radius = max(1, SSGI_DENOISE_RADIUS);

        float2 dir = normalize(max(abs(_SSGIDenoiseDir.xy), 1e-5) * sign(_SSGIDenoiseDir.xy + 1e-5));
        [loop]
        for (int k = -radius; k <= radius; k++)
        {
            float2 suv = uv + dir * texel * k;
            if (suv.x <= 0.0 || suv.x >= 1.0 || suv.y <= 0.0 || suv.y >= 1.0)
                continue;

            float3 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, suv).rgb;
            float d = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
            float3 n = normalize(SampleSceneNormals(suv));

            float spatial = exp(-0.5 * (k * k) / max(1.0, radius * radius * 0.5));
            float depthW = exp(-abs(d - centerDepth) * SSGI_DENOISE_DEPTH_SIGMA);
            float normalW = pow(saturate(dot(centerNormal, n)), SSGI_DENOISE_NORMAL_POWER);
            float w = spatial * depthW * normalW;

            sum += c * w;
            wsum += w;
        }

        return (wsum > 1e-4) ? (sum / wsum) : center;
    }

    float3 TemporalAccum(float2 uv)
    {
        float3 current = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
        if (_SSGIHistoryValid == 0 || _SSGIParamsTemporal.x < 0.5)
            return current;

        float2 motion = SAMPLE_TEXTURE2D(_MotionVectorTexture, sampler_MotionVectorTexture, uv).xy;
        float2 prevUVFromMotion = uv - motion;
        float2 prevUV = prevUVFromMotion;
        if (prevUV.x <= 0.0 || prevUV.x >= 1.0 || prevUV.y <= 0.0 || prevUV.y >= 1.0)
            return current;

        float motionMag = length(motion);
        if (motionMag > SSGI_TEMPORAL_MOTION_REJECT)
            return current;

        float3 history = SAMPLE_TEXTURE2D(_SSGIHistoryTex, sampler_SSGIHistoryTex, prevUV).rgb;

        // 3x3 neighborhood clamp to reduce ghosting / disocclusion streaks.
        float2 texel = _SSGIScreenSize.zw;
        float3 nMin = current;
        float3 nMax = current;
        [unroll]
        for (int j = -1; j <= 1; j++)
        {
            [unroll]
            for (int i = -1; i <= 1; i++)
            {
                float2 nuv = uv + float2(i, j) * texel;
                float3 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, saturate(nuv)).rgb;
                nMin = min(nMin, c);
                nMax = max(nMax, c);
            }
        }

        float response = saturate(SSGI_TEMPORAL_RESPONSE);
        float3 center = 0.5 * (nMin + nMax);
        float3 extent = max(0.01, (nMax - nMin) * max(0.5, SSGI_TEMPORAL_CLAMP_SCALE) + 0.02);
        history = clamp(history, center - extent, center + extent);
        return lerp(current, history, response);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "SSGI_Raw"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragRaw
            half4 FragRaw(Varyings input) : SV_Target
            {
                float3 indirect = ComputeRawIndirect(input.uv);
                return half4(indirect, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "SSGI_Denoise"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDenoise
            half4 FragDenoise(Varyings input) : SV_Target
            {
                if (_SSGIParamsDenoise.x < 0.5)
                    return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                float3 filtered = DenoiseBilateral(input.uv);
                return half4(filtered, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "SSGI_Temporal"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragTemporal
            half4 FragTemporal(Varyings input) : SV_Target
            {
                float3 temporal = TemporalAccum(input.uv);
                return half4(temporal, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "SSGI_Composite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            half4 FragComposite(Varyings input) : SV_Target
            {
                float3 baseCol = SAMPLE_TEXTURE2D(_SSGISourceTex, sampler_SSGISourceTex, input.uv).rgb;
                float3 indirect = SAMPLE_TEXTURE2D(_SSGIIndirectTex, sampler_SSGIIndirectTex, input.uv).rgb;
                return half4(baseCol + indirect * SSGI_INTENSITY, 1.0);
            }
            ENDHLSL
        }
    }
}
