Shader "Hidden/SSR_StepDebug"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

    TEXTURE2D(_BaseMap);
    SAMPLER(sampler_BaseMap);

    float4 _SSRScreenSize;
    float4 _SSRParams;    // x: intensity, y: maxSteps, z: stride, w: thickness
    float4 _SSRParams2;   // x: maxDistance, y: rayStartBias, z: fresnelPower
    float4 _SSRReceiverPlane;
    float4 _SSRReceiverParams;
    float4x4 _SSRInvViewProj;
    float4x4 _SSRViewProj;
    float4x4 _SSRView;

    #define SSR_MAX_STEPS int(_SSRParams.y)
    #define SSR_STRIDE _SSRParams.z
    #define SSR_THICKNESS _SSRParams.w
    #define SSR_MAX_DISTANCE _SSRParams2.x
    #define SSR_RAY_START_BIAS _SSRParams2.y
    #define SSR_RECEIVER_NORMAL_THRESHOLD _SSRReceiverParams.x
    #define SSR_RECEIVER_MAX_DISTANCE _SSRReceiverParams.y
    #define SSR_USE_RECEIVER_MASK _SSRReceiverParams.z

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
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "Step0_None"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return half4(0,0,0,1); }
            ENDHLSL
        }

        Pass
        {
            Name "Step1_Depth"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                float rawDepth = SampleSceneDepth(i.uv);
                float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float normalized = linearDepth / 100.0;
                return half4(normalized.xxx, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Step2_Placeholder"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return half4(0,0,0,1); }
            ENDHLSL
        }

        Pass
        {
            Name "Step3_Placeholder"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return half4(0,0,0,1); }
            ENDHLSL
        }

        Pass
        {
            Name "Step4_Placeholder"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return half4(0,0,0,1); }
            ENDHLSL
        }

        Pass
        {
            Name "Step5_ViewPosVector"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                float rawDepth = SampleSceneDepth(i.uv);
                if (rawDepth < 0.00001)
                    return half4(0,0,0,1);
                float3 worldPos = ComputeWorldSpacePosition(i.uv, rawDepth, _SSRInvViewProj);
                return half4(frac(worldPos * 0.1), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Step6_Placeholder"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return half4(0,0,0,1); }
            ENDHLSL
        }

        Pass
        {
            Name "Step7_FinalResult"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv); }
            ENDHLSL
        }

        Pass
        {
            Name "Step8_Combine"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            TEXTURE2D(_Step1); SAMPLER(sampler_Step1);
            TEXTURE2D(_Step2); SAMPLER(sampler_Step2);
            TEXTURE2D(_Step3); SAMPLER(sampler_Step3);
            TEXTURE2D(_Step4); SAMPLER(sampler_Step4);

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                if (uv.x < 0.5 && uv.y < 0.5)
                    return SAMPLE_TEXTURE2D(_Step1, sampler_Step1, uv * 2.0);
                if (uv.x >= 0.5 && uv.y < 0.5)
                    return SAMPLE_TEXTURE2D(_Step2, sampler_Step2, float2((uv.x - 0.5) * 2.0, uv.y * 2.0));
                if (uv.x < 0.5 && uv.y >= 0.5)
                    return SAMPLE_TEXTURE2D(_Step3, sampler_Step3, float2(uv.x * 2.0, (uv.y - 0.5) * 2.0));
                return SAMPLE_TEXTURE2D(_Step4, sampler_Step4, float2((uv.x - 0.5) * 2.0, (uv.y - 0.5) * 2.0));
            }
            ENDHLSL
        }

        Pass
        {
            Name "Step9_Placeholder"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return half4(0,0,0,1); }
            ENDHLSL
        }

        Pass
        {
            Name "Step10_Normals"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                float rawDepth = SampleSceneDepth(i.uv);
                if (rawDepth < 0.00001)
                    return half4(0,0,0,1);
                half3 n = normalize(SampleSceneNormals(i.uv) * 2.0 - 1.0);
                return half4(n * 0.5 + 0.5, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Step11_Placeholder"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return half4(0,0,0,1); }
            ENDHLSL
        }

        Pass
        {
            Name "Step12_Placeholder"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return half4(0,0,0,1); }
            ENDHLSL
        }

        Pass
        {
            Name "Step13_Placeholder"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return half4(0,0,0,1); }
            ENDHLSL
        }

        Pass
        {
            Name "Step14_Placeholder"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return half4(0,0,0,1); }
            ENDHLSL
        }

        Pass
        {
            Name "Step15_Placeholder"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return half4(0,0,0,1); }
            ENDHLSL
        }

        Pass
        {
            Name "Step16_WorldPosition"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                float rawDepth = SampleSceneDepth(i.uv);
                if (rawDepth < 0.00001)
                    return half4(0,0,0,1);
                float3 worldPos = ComputeWorldSpacePosition(i.uv, rawDepth, _SSRInvViewProj);
                float3 normalized = float3(worldPos.x * 0.05 + 0.5, worldPos.y * 0.1, worldPos.z * 0.05 + 0.5);
                return half4(saturate(normalized), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Step17_ReflectionUV"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                float rawDepth = SampleSceneDepth(i.uv);
                if (rawDepth < 0.00001)
                    return half4(0, 0, 0, 1);

                float3 worldPos = ComputeWorldSpacePosition(i.uv, rawDepth, _SSRInvViewProj);
                float3 normalWS = normalize(SampleSceneNormals(i.uv) * 2.0 - 1.0);
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - worldPos);
                float3 reflectDirWS = normalize(reflect(-viewDirWS, normalWS));
                float3 sampleWS = worldPos + reflectDirWS * max(SSR_RAY_START_BIAS, 0.2);

                float4 sampleCS = mul(_SSRViewProj, float4(sampleWS, 1.0));
                if (sampleCS.w <= 0.00001)
                    return half4(0, 0, 0, 1);

                float2 sampleUV = sampleCS.xy / sampleCS.w * 0.5 + 0.5;
                return half4(sampleUV, 0, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Step18_HitMask"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                float rawDepth = SampleSceneDepth(uv);
                if (rawDepth < 0.00001)
                    return half4(0, 0, 0, 1);

                float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, _SSRInvViewProj);
                float3 normalWS = normalize(SampleSceneNormals(uv) * 2.0 - 1.0);
                float3 planeN = normalize(_SSRReceiverPlane.xyz);
                if (SSR_USE_RECEIVER_MASK > 0.5)
                {
                    float normalDot = dot(normalWS, planeN);
                    if (normalDot < SSR_RECEIVER_NORMAL_THRESHOLD)
                        return half4(0, 0, 0, 1);

                    float distToPlane = abs(dot(worldPos, planeN) - _SSRReceiverPlane.w);
                    if (distToPlane > SSR_RECEIVER_MAX_DISTANCE)
                        return half4(0, 0, 0, 1);
                }

                float3 viewPosVS = mul(_SSRView, float4(worldPos, 1.0)).xyz;
                float3 normalVS = normalize(mul((float3x3)_SSRView, normalWS));
                float3 rayDirVS = normalize(reflect(normalize(viewPosVS), normalVS));

                float t = max(SSR_RAY_START_BIAS, 0.01);
                float stepSize = clamp(SSR_STRIDE, 0.02, 0.1);
                float baseThickness = max(SSR_THICKNESS, 0.005);
                float3 curVS = viewPosVS + normalVS * 0.02 + rayDirVS * t;
                float prevDiff = 1e6;
                bool hit = false;

                [loop]
                for (int step = 0; step < max(64, SSR_MAX_STEPS); step++)
                {
                    curVS += rayDirVS * stepSize;
                    t += stepSize;

                    float rayDepthVS = -curVS.z;
                    if (rayDepthVS <= 0.001 || rayDepthVS > SSR_MAX_DISTANCE)
                        break;

                    float4 sampleCS = mul(_SSRViewProj, float4(curVS, 1.0));
                    if (sampleCS.w <= 0.00001)
                        break;

                    float2 sampleUV = sampleCS.xy / sampleCS.w * 0.5 + 0.5;
                    if (sampleUV.x <= 0.0 || sampleUV.x >= 1.0 || sampleUV.y <= 0.0 || sampleUV.y >= 1.0)
                        break;

                    float sceneRawDepth = SampleSceneDepth(sampleUV);
                    if (sceneRawDepth < 0.00001)
                    {
                        continue;
                    }

                    float sceneDepthVS = LinearEyeDepth(sceneRawDepth, _ZBufferParams);
                    float thickness = baseThickness * (1.0 + sceneDepthVS * 0.03);
                    float diff = sceneDepthVS - rayDepthVS;
                    bool crossed = (diff <= 0.0 && prevDiff > 0.0);
                    bool insideThickness = (abs(diff) <= thickness);
                    if (crossed || insideThickness)
                    {
                        hit = true;
                        break;
                    }

                    prevDiff = diff;
                }

                return hit ? half4(1, 1, 1, 1) : half4(0, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
