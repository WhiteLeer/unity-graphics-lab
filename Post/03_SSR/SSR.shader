Shader "Hidden/SSR"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

    TEXTURE2D(_BaseMap);
    SAMPLER(sampler_BaseMap);

    float4 _SSRParams;    // x: intensity, y: maxSteps, z: stride, w: thickness
    float4 _SSRParams2;   // x: maxDistance, y: rayStartBias, z: fresnelPower
    float4 _SSRParams3;   // x: fadeStart, y: fadeEnd, z: hitSoftness
    float4 _SSRScreenSize; // x: width, y: height, z: 1/width, w: 1/height
    int _SSRDebugMode; // 0=none, 16=worldPos, 17=hitUV, 18=hitMask

    float4x4 _SSRView;
    float4x4 _SSRProj;
    float4x4 _SSRInvViewProj;

    #define SSR_INTENSITY _SSRParams.x
    #define SSR_MAX_STEPS int(_SSRParams.y)
    #define SSR_STRIDE _SSRParams.z
    #define SSR_THICKNESS _SSRParams.w

    #define SSR_MAX_DISTANCE _SSRParams2.x
    #define SSR_RAY_START_BIAS _SSRParams2.y
    #define SSR_FRESNEL_POWER _SSRParams2.z

    #define SSR_FADE_START _SSRParams3.x
    #define SSR_FADE_END _SSRParams3.y
    #define SSR_HIT_SOFTNESS _SSRParams3.z

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

    float EdgeFade(float2 uv, float fadeStart, float fadeEnd)
    {
        float a = min(fadeStart, fadeEnd);
        float b = max(fadeStart, fadeEnd);
        if (abs(b - a) < 0.001)
            return 1.0;
        float2 dist = min(uv, 1.0 - uv);
        float minDist = min(dist.x, dist.y);
        return smoothstep(a, b, minDist);
    }

    float2 ClipToScreenUV(float4 clipPos)
    {
        float2 uv = clipPos.xy / clipPos.w * 0.5 + 0.5;
        if (_ProjectionParams.x < 0.0)
            uv.y = 1.0 - uv.y;
        return uv;
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "SSR"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment SSR_Frag

            half4 SSR_Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                half4 sceneColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);

                float rawDepth = SampleSceneDepth(uv);
                if (rawDepth < 0.00001)
                    return sceneColor;

                float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, _SSRInvViewProj);
                float3 normalWS = normalize(SampleSceneNormals(uv));
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - worldPos);
                float3 reflectDirWS = normalize(reflect(-viewDirWS, normalWS));

                if (_SSRDebugMode == 16)
                {
                    float3 wp = float3(worldPos.x * 0.05 + 0.5, worldPos.y * 0.1, worldPos.z * 0.05 + 0.5);
                    return half4(saturate(wp), 1);
                }

                float nLen = dot(normalWS, normalWS);
                if (nLen < 0.25)
                    return sceneColor;

                float stepLength = max(SSR_STRIDE, 0.02);
                float startBias = max(SSR_RAY_START_BIAS, 0.005);

                bool hit = false;
                float2 hitUV = uv;
                float3 hitColor = 0;
                float hitDistance = 0.0;
                float hitWeight = 0.0;

                float3 viewPosVS = mul(_SSRView, float4(worldPos, 1.0)).xyz;
                float3 normalVS = normalize(mul((float3x3)_SSRView, normalWS));
                float3 rayDirVS = normalize(mul((float3x3)_SSRView, reflectDirWS));
                if (rayDirVS.z >= -0.0001)
                    return sceneColor;

                float3 rayOriginVS = viewPosVS + normalVS * startBias;
                float prevTravel = startBias;
                float prevDiff = 0.0;
                bool hasPrevDiff = false;

                [loop]
                for (int i = 0; i < max(SSR_MAX_STEPS, 1); i++)
                {
                    float travel = startBias + stepLength * (i + 1);
                    if (travel > SSR_MAX_DISTANCE)
                        break;

                    float3 sampleVS = rayOriginVS + rayDirVS * travel;
                    float rayDepthVS = -sampleVS.z;
                    if (rayDepthVS <= 0.001)
                        break;

                    float4 sampleCS = mul(_SSRProj, float4(sampleVS, 1.0));
                    if (sampleCS.w <= 0.00001)
                        break;

                    float2 sampleUV = ClipToScreenUV(sampleCS);
                    if (sampleUV.x <= 0.0 || sampleUV.x >= 1.0 || sampleUV.y <= 0.0 || sampleUV.y >= 1.0)
                        break;

                    float sceneRawDepth = SampleSceneDepth(sampleUV);
                    if (sceneRawDepth < 0.00001)
                        continue;

                    float sceneDepthVS = LinearEyeDepth(sceneRawDepth, _ZBufferParams);
                    float diff = sceneDepthVS - rayDepthVS;
                    bool crossed = hasPrevDiff && (prevDiff > 0.0) && (diff <= 0.0);
                    if (crossed)
                    {
                        float minTravel = prevTravel;
                        float maxTravel = travel;

                        [unroll(5)]
                        for (int r = 0; r < 5; r++)
                        {
                            float midTravel = 0.5 * (minTravel + maxTravel);
                            float3 midVS = rayOriginVS + rayDirVS * midTravel;
                            float4 midCS = mul(_SSRProj, float4(midVS, 1.0));
                            if (midCS.w <= 0.00001)
                                break;

                            float2 midUV = ClipToScreenUV(midCS);
                            if (midUV.x <= 0.0 || midUV.x >= 1.0 || midUV.y <= 0.0 || midUV.y >= 1.0)
                                break;

                            float midRawDepth = SampleSceneDepth(midUV);
                            if (midRawDepth < 0.00001)
                                break;

                            float midSceneDepth = LinearEyeDepth(midRawDepth, _ZBufferParams);
                            float midDiff = midSceneDepth - (-midVS.z);
                            if (midDiff > 0.0)
                                minTravel = midTravel;
                            else
                                maxTravel = midTravel;
                        }

                        float3 refinedVS = rayOriginVS + rayDirVS * maxTravel;
                        float4 refinedCS = mul(_SSRProj, float4(refinedVS, 1.0));
                        if (refinedCS.w <= 0.00001)
                            break;

                        float2 refinedUV = ClipToScreenUV(refinedCS);
                        if (refinedUV.x <= 0.0 || refinedUV.x >= 1.0 || refinedUV.y <= 0.0 || refinedUV.y >= 1.0)
                            break;

                        float refinedRawDepth = SampleSceneDepth(refinedUV);
                        if (refinedRawDepth < 0.00001)
                            continue;

                        float refinedSceneDepth = LinearEyeDepth(refinedRawDepth, _ZBufferParams);
                        float refinedDiff = refinedSceneDepth - (-refinedVS.z);
                        float thickness = max(SSR_THICKNESS, 0.001) * (1.0 + refinedSceneDepth * 0.02);
                        if (refinedDiff > 0.0 || refinedDiff < -thickness)
                            continue;

                        float2 uvDeltaPx = abs((refinedUV - uv) * _SSRScreenSize.xy);
                        if (max(uvDeltaPx.x, uvDeltaPx.y) < 1.0)
                            continue;

                        float3 hitNormalWS = normalize(SampleSceneNormals(refinedUV));
                        if (dot(hitNormalWS, -reflectDirWS) <= 0.05)
                            continue;

                        int2 hitPx = int2(refinedUV * _SSRScreenSize.xy);
                        int2 maxPx = int2(_SSRScreenSize.xy) - 1;
                        hitPx = clamp(hitPx, int2(0, 0), maxPx);

                        float depthConfidence = saturate(1.0 - abs(refinedDiff) / max(thickness, 1e-4));
                        float spreadConfidence = saturate((max(uvDeltaPx.x, uvDeltaPx.y) - 0.75) / 2.0);
                        float rawHitWeight = depthConfidence * spreadConfidence;
                        float feather = lerp(0.03, 0.35, saturate(SSR_HIT_SOFTNESS));
                        float softHitWeight = smoothstep(0.0, feather, rawHitWeight);

                        hit = true;
                        hitUV = refinedUV;
                        hitColor = LOAD_TEXTURE2D(_BaseMap, hitPx).rgb;
                        hitDistance = maxTravel;
                        hitWeight = softHitWeight;
                        break;
                    }

                    prevTravel = travel;
                    prevDiff = diff;
                    hasPrevDiff = true;
                }

                if (!hit)
                {
                    if (_SSRDebugMode == 18)
                        return half4(0, 0, 0, 1);
                    return sceneColor;
                }

                if (_SSRDebugMode == 17)
                    return half4(hitUV, 0, 1);
                if (_SSRDebugMode == 18)
                    return half4(1, 1, 1, 1);

                float fadeSrc = EdgeFade(uv, SSR_FADE_START, SSR_FADE_END);
                float fadeHit = EdgeFade(hitUV, SSR_FADE_START, SSR_FADE_END);
                float edgeFade = sqrt(saturate(fadeSrc * fadeHit));
                float cosTheta = saturate(dot(viewDirWS, normalWS));
                float fresnelExp = max(0.5, SSR_FRESNEL_POWER);
                float fresnelTerm = pow(1.0 - cosTheta, fresnelExp);
                float reflectance = lerp(0.04, 1.0, saturate(fresnelTerm));
                float distanceFade = saturate(1.0 - hitDistance / max(SSR_MAX_DISTANCE, 0.001));
                float strength = SSR_INTENSITY * edgeFade * distanceFade * reflectance * hitWeight;
                float mirrorWeight = saturate(strength);
                float3 reflectedColor = hitColor;
                float3 finalColor = lerp(sceneColor.rgb, reflectedColor, mirrorWeight);
                finalColor = saturate(finalColor);
                return half4(finalColor, sceneColor.a);
            }
            ENDHLSL
        }
    }
}
