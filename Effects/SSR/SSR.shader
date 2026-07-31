Shader "Hidden/SSR_ReflectionProbe"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

    TEXTURE2D(_BaseMap);
    SAMPLER(sampler_BaseMap);
    TEXTURE2D(_SSRReceiverRoughnessMap);
    SAMPLER(sampler_SSRReceiverRoughnessMap);

    float4 _SSRParams;    // x: intensity, y: maxDistance, z: step, w: thickness
    float4 _SSRScreenSize; // x: width, y: height, z: 1/width, w: 1/height
    int _SSRDebugMode; // 0=none, 16=worldPos, 17=hitUV, 18=hitMask
    float _SSRDepthScale;
    float4 _SSRExtraParams; // x: receiver normal threshold, y: ray start bias, z: reflection blend, w: receiver normal fade
    float4 _SSRExtraParams2; // x: enable receiver filter, y: fallback intensity, z: fallback roughness
    float4 _SSRReceiverRoughnessST; // x: tile x, y: tile y, z: offset x, w: offset y
    float4 _SSRReceiverRoughnessParams; // x: roughness strength, y: max blur pixels
    float4 _SSRAmbientSkyColor;
    float4 _SSRAmbientEquatorColor;
    float4 _SSRAmbientGroundColor;

    float4x4 _SSRView;
    float4x4 _SSRProj;
    float4x4 _SSRInvViewProj;

    #define SSR_INTENSITY _SSRParams.x
    #define SSR_MAX_DISTANCE _SSRParams.y
    #define SSR_STEP _SSRParams.z
    #define SSR_THICKNESS _SSRParams.w
    #define SSR_RECEIVER_THRESHOLD _SSRExtraParams.x
    #define SSR_RAY_START_BIAS _SSRExtraParams.y
    #define SSR_REFLECTION_BLEND _SSRExtraParams.z
    #define SSR_RECEIVER_FADE _SSRExtraParams.w
    #define SSR_ENABLE_RECEIVER_FILTER _SSRExtraParams2.x
    #define SSR_FALLBACK_INTENSITY _SSRExtraParams2.y
    #define SSR_FALLBACK_ROUGHNESS _SSRExtraParams2.z
    #define SSR_RECEIVER_ROUGHNESS_STRENGTH _SSRReceiverRoughnessParams.x
    #define SSR_RECEIVER_MAX_BLUR_PIXELS _SSRReceiverRoughnessParams.y

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

    float2 ClipToScreenUV(float4 clipPos)
    {
        float2 uv = clipPos.xy / clipPos.w * 0.5 + 0.5;
        if (_ProjectionParams.x < 0.0)
            uv.y = 1.0 - uv.y;
        return uv;
    }
    float EvaluateReceiverMask(float normalY)
    {
        if (SSR_ENABLE_RECEIVER_FILTER <= 0.5)
            return 1.0;

        float fade = max(SSR_RECEIVER_FADE, 1e-5);
        return smoothstep(SSR_RECEIVER_THRESHOLD - fade, SSR_RECEIVER_THRESHOLD, normalY);
    }

    half3 SampleFallbackEnvironment(float3 reflectDirWS, half perceptualRoughness)
    {
        half up = saturate(reflectDirWS.y * 0.5h + 0.5h);
        half3 lower = lerp(_SSRAmbientGroundColor.rgb, _SSRAmbientEquatorColor.rgb, saturate(up * 2.0h));
        half3 upper = lerp(_SSRAmbientEquatorColor.rgb, _SSRAmbientSkyColor.rgb, saturate((up - 0.5h) * 2.0h));
        half3 gradient = (up < 0.5h) ? lower : upper;
        half blur = saturate(perceptualRoughness);
        return lerp(gradient, _SSRAmbientEquatorColor.rgb, blur * 0.35h);
    }

    float SampleReceiverRoughness(float3 worldPos)
    {
        float2 roughnessUV = worldPos.xz * _SSRReceiverRoughnessST.xy + _SSRReceiverRoughnessST.zw;
        float roughness = SAMPLE_TEXTURE2D(_SSRReceiverRoughnessMap, sampler_SSRReceiverRoughnessMap, roughnessUV).r;
        return saturate(roughness * SSR_RECEIVER_ROUGHNESS_STRENGTH);
    }

    float3 SampleReflectionBlur(float2 centerUV, float roughness)
    {
        float blurPixels = roughness * roughness * SSR_RECEIVER_MAX_BLUR_PIXELS;
        if (blurPixels <= 0.001)
            return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, centerUV).rgb;

        float2 texel = _SSRScreenSize.zw;
        float2 axis1 = texel * blurPixels;
        float2 axis2 = texel * blurPixels * 2.0;
        float2 diag1 = axis1 * 0.70710678;

        float3 accum = 0.0;
        float weight = 0.0;
        float3 center = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, centerUV).rgb;
        accum += center * 0.18;
        weight += 0.18;

        float2 offsets[12] =
        {
            float2( axis1.x, 0.0), float2(-axis1.x, 0.0),
            float2(0.0,  axis1.y), float2(0.0, -axis1.y),
            float2( diag1.x,  diag1.y), float2(-diag1.x,  diag1.y),
            float2( diag1.x, -diag1.y), float2(-diag1.x, -diag1.y),
            float2( axis2.x, 0.0), float2(-axis2.x, 0.0),
            float2(0.0,  axis2.y), float2(0.0, -axis2.y)
        };

        float weights[12] =
        {
            0.11, 0.11,
            0.11, 0.11,
            0.07, 0.07,
            0.07, 0.07,
            0.03, 0.03,
            0.03, 0.03
        };

        [unroll]
        for (int i = 0; i < 12; ++i)
        {
            float2 sampleUV = saturate(centerUV + offsets[i]);
            accum += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, sampleUV).rgb * weights[i];
            weight += weights[i];
        }

        return accum / max(weight, 1e-5);
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

                if (SSR_INTENSITY <= 0.0001 || SSR_REFLECTION_BLEND <= 0.0001)
                    return sceneColor;

                float rawDepth = SampleSceneDepth(uv);
                if (rawDepth < 0.00001 || rawDepth > 0.99999)
                    return sceneColor;

                float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, _SSRInvViewProj);
                float3 normalWS = normalize(SampleSceneNormals(uv));
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - worldPos);
                float3 reflectDirWS = normalize(reflect(-viewDirWS, normalWS));
                float receiverRoughness = SampleReceiverRoughness(worldPos);
                half fresnelTerm = Pow4(1.0 - saturate(dot(normalWS, viewDirWS)));
                float receiverMask = EvaluateReceiverMask(normalWS.y);
                if (receiverMask < 0.5)
                    return sceneColor;

                if (_SSRDebugMode == 16)
                {
                    float3 wp = float3(worldPos.x * 0.05 + 0.5, worldPos.y * 0.1, worldPos.z * 0.05 + 0.5);
                    return half4(saturate(wp), 1);
                }

                float nLen = dot(normalWS, normalWS);
                if (nLen < 0.25)
                    return sceneColor;

                bool hit = false;
                bool exitedScreen = false;
                float2 hitUV = uv;
                float3 hitColor = 0;
                half3 fallbackReflection = SampleFallbackEnvironment(reflectDirWS, saturate(SSR_FALLBACK_ROUGHNESS + receiverRoughness));

                float3 viewPosVS = mul(_SSRView, float4(worldPos, 1.0)).xyz;
                float3 normalVS = normalize(mul((float3x3)_SSRView, normalWS));
                float3 reflectedVS = normalize(mul((float3x3)_SSRView, reflectDirWS));
                if (reflectedVS.z >= -0.0001)
                    return sceneColor;

                float atten = 0.0;

                float startBias = max(SSR_STEP, SSR_RAY_START_BIAS);
                float prevMarchDistance = startBias;
                [loop]
                for (float marchDistance = startBias; marchDistance < SSR_MAX_DISTANCE; marchDistance += SSR_STEP)
                {
                    float3 marchReflectionVS = marchDistance * reflectedVS;
                    float targetDepth = -(viewPosVS + marchReflectionVS).z * _SSRDepthScale;
                    if (targetDepth <= 0.001)
                    {
                        exitedScreen = true;
                        break;
                    }

                    float4 sampleCS = mul(_SSRProj, float4(viewPosVS + marchReflectionVS, 1.0));
                    if (sampleCS.w <= 0.00001)
                    {
                        exitedScreen = true;
                        break;
                    }

                    float2 target = ClipToScreenUV(sampleCS);
                    if (target.x <= 0.0 || target.x >= 1.0 || target.y <= 0.0 || target.y >= 1.0)
                    {
                        exitedScreen = true;
                        break;
                    }

                    float sampledRawDepth = SampleSceneDepth(target);
                    if (sampledRawDepth < 0.00001 || sampledRawDepth > 0.99999)
                        break;

                    float3 sampledWorldPos = ComputeWorldSpacePosition(target, sampledRawDepth, _SSRInvViewProj);
                    float3 sampledViewPosVS = mul(_SSRView, float4(sampledWorldPos, 1.0)).xyz;
                    float sampledDepth = (-sampledViewPosVS.z) * _SSRDepthScale;
                    float3 sampledNormalWS = normalize(SampleSceneNormals(target));
                    if (EvaluateReceiverMask(sampledNormalWS.y) >= 0.5)
                    {
                        prevMarchDistance = marchDistance;
                        continue;
                    }
                    float depthDelta = sampledDepth - targetDepth;
                    if (depthDelta > 0.0 && depthDelta < SSR_THICKNESS)
                    {
                        float refineLow = prevMarchDistance;
                        float refineHigh = marchDistance;
                        float2 refinedUV = target;
                        [unroll]
                        for (int refineStep = 0; refineStep < 5; ++refineStep)
                        {
                            float midDistance = 0.5 * (refineLow + refineHigh);
                            float3 midReflectionVS = midDistance * reflectedVS;
                            float4 midCS = mul(_SSRProj, float4(viewPosVS + midReflectionVS, 1.0));
                            if (midCS.w <= 0.00001)
                            {
                                refineLow = midDistance;
                                continue;
                            }

                            float2 midUV = ClipToScreenUV(midCS);
                            if (midUV.x <= 0.0 || midUV.x >= 1.0 || midUV.y <= 0.0 || midUV.y >= 1.0)
                            {
                                refineLow = midDistance;
                                continue;
                            }

                            float midRawDepth = SampleSceneDepth(midUV);
                            if (midRawDepth < 0.00001 || midRawDepth > 0.99999)
                            {
                                refineLow = midDistance;
                                continue;
                            }

                            float midTargetDepth = -(viewPosVS + midReflectionVS).z * _SSRDepthScale;
                            float3 midWorldPos = ComputeWorldSpacePosition(midUV, midRawDepth, _SSRInvViewProj);
                            float3 midViewPosVS = mul(_SSRView, float4(midWorldPos, 1.0)).xyz;
                            float midSampledDepth = (-midViewPosVS.z) * _SSRDepthScale;
                            float3 midNormalWS = normalize(SampleSceneNormals(midUV));
                            if (EvaluateReceiverMask(midNormalWS.y) >= 0.5)
                            {
                                refineLow = midDistance;
                                continue;
                            }
                            float midDepthDelta = midSampledDepth - midTargetDepth;

                            if (midDepthDelta > 0.0)
                            {
                                refineHigh = midDistance;
                                refinedUV = midUV;
                            }
                            else
                            {
                                refineLow = midDistance;
                            }
                        }

                        hit = true;
                        hitUV = refinedUV;
                        int2 hitPx = int2(hitUV * _SSRScreenSize.xy);
                        int2 maxPx = int2(_SSRScreenSize.xy) - 1;
                        hitPx = clamp(hitPx, int2(0, 0), maxPx);
                        hitColor = SampleReflectionBlur(hitUV, receiverRoughness);
                        atten = saturate(1.0 - refineHigh / SSR_MAX_DISTANCE);
                        break;
                    }

                    prevMarchDistance = marchDistance;
                    if (targetDepth > 1.0)
                    {
                        atten = 1.0;
                        break;
                    }
                }

                if (!hit)
                {
                    if (!exitedScreen)
                        return sceneColor;

                    float fallbackWeight = saturate(receiverMask * SSR_FALLBACK_INTENSITY * (0.15 + 0.85 * fresnelTerm));
                    float3 fallbackColor = lerp(sceneColor.rgb, fallbackReflection, fallbackWeight);
                    return half4(fallbackColor, sceneColor.a);
                }

                float reflectionWeight = saturate(SSR_INTENSITY * receiverMask * SSR_REFLECTION_BLEND);
                float3 finalColor = lerp(sceneColor.rgb, hitColor, reflectionWeight);
                return half4(finalColor, sceneColor.a);
            }
            ENDHLSL
        }
    }
}
