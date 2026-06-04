Shader "Hidden/SSAO_StepDebug"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

    TEXTURE2D(_BaseMap);
    SAMPLER(sampler_BaseMap);

    // SSAO参数
    float4 _SSAOParams;
    float4 _SourceSize;
    float4 _ProjectionParams2;
    float4 _CameraViewTopLeftCorner;
    float4 _CameraViewXExtent;
    float4 _CameraViewYExtent;

    #define INTENSITY _SSAOParams.x
    #define RADIUS _SSAOParams.y

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

    // Unity官方方法：重建从相机到像素的世界空间向量
    float3 ReconstructViewPos(float2 uv, float depth)
    {
        uv.y = 1.0 - uv.y;
        float zScale = depth * _ProjectionParams2.x;
        float3 viewPos = _CameraViewTopLeftCorner.xyz
            + _CameraViewXExtent.xyz * uv.x
            + _CameraViewYExtent.xyz * uv.y;
        viewPos *= zScale;
        return viewPos;
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        // Pass 0: None（占位）
        Pass
        {
            Name "Step0_None"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return half4(0, 0, 0, 1); }
            ENDHLSL
        }

        // Pass 1: 深度可视化
        Pass
        {
            Name "Step1_Depth"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                float rawDepth = SampleSceneDepth(input.uv);
                float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float normalized = linearDepth / 100.0;
                return half4(normalized.xxx, 1.0);
            }
            ENDHLSL
        }

        // Pass 2-4: 占位
        Pass
        {
            Name "Step2_Placeholder"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return half4(0, 0, 0, 1); }
            ENDHLSL
        }
        Pass
        {
            Name "Step3_Placeholder"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return half4(0, 0, 0, 1); }
            ENDHLSL
        }
        Pass
        {
            Name "Step4_Placeholder"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return half4(0, 0, 0, 1); }
            ENDHLSL
        }

        // Pass 5: 显示从相机到该点的世界空间向量
        Pass
        {
            Name "Step5_ViewPosVector"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                float rawDepth = SampleSceneDepth(input.uv);
                if (rawDepth < 0.00001)
                    return half4(0, 0, 0, 1);

                float depth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float3 viewPosVec = ReconstructViewPos(input.uv, depth);

                float3 visPos = frac(viewPosVec * 0.1);
                return half4(visPos, 1);
            }
            ENDHLSL
        }

        // Pass 6: 显示原始AO值（占位）
        Pass
        {
            Name "Step6_RawAO"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return half4(1, 1, 1, 1); }
            ENDHLSL
        }

        // Pass 7: 显示最终AO结果
        Pass
        {
            Name "Step7_FinalAO"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                half4 sceneColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                return sceneColor;
            }
            ENDHLSL
        }

        // Pass 8: 合并4个步骤到2x2网格
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

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;

                if (uv.x < 0.5 && uv.y < 0.5)
                {
                    return SAMPLE_TEXTURE2D(_Step1, sampler_Step1, uv * 2.0);
                }
                else if (uv.x >= 0.5 && uv.y < 0.5)
                {
                    return SAMPLE_TEXTURE2D(_Step2, sampler_Step2, float2((uv.x - 0.5) * 2.0, uv.y * 2.0));
                }
                else if (uv.x < 0.5 && uv.y >= 0.5)
                {
                    return SAMPLE_TEXTURE2D(_Step3, sampler_Step3, float2(uv.x * 2.0, (uv.y - 0.5) * 2.0));
                }
                else
                {
                    return SAMPLE_TEXTURE2D(_Step4, sampler_Step4, float2((uv.x - 0.5) * 2.0, (uv.y - 0.5) * 2.0));
                }
            }
            ENDHLSL
        }

        // Pass 9: 占位
        Pass
        {
            Name "Step9_Placeholder"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return half4(0, 0, 0, 1); }
            ENDHLSL
        }

        // Pass 10: 显示世界空间法线
        Pass
        {
            Name "Step10_Normals"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                float rawDepth = SampleSceneDepth(input.uv);
                if (rawDepth < 0.00001)
                    return half4(0, 0, 0, 1);

                half3 normal = SampleSceneNormals(input.uv);
                return half4(normal * 0.5 + 0.5, 1);
            }
            ENDHLSL
        }

        // Pass 11-15: 占位
        Pass
        {
            Name "Step11_Placeholder"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return half4(0, 0, 0, 1); }
            ENDHLSL
        }
        Pass
        {
            Name "Step12_Placeholder"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return half4(0, 0, 0, 1); }
            ENDHLSL
        }
        Pass
        {
            Name "Step13_Placeholder"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return half4(0, 0, 0, 1); }
            ENDHLSL
        }
        Pass
        {
            Name "Step14_Placeholder"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return half4(0, 0, 0, 1); }
            ENDHLSL
        }
        Pass
        {
            Name "Step15_Placeholder"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return half4(0, 0, 0, 1); }
            ENDHLSL
        }

        // Pass 16: 显示完整的世界坐标
        Pass
        {
            Name "Step16_WorldPosition"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                float rawDepth = SampleSceneDepth(input.uv);
                if (rawDepth < 0.00001)
                    return half4(0, 0, 0, 1);

                float depth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float3 viewPosVec = ReconstructViewPos(input.uv, depth);

                // 计算完整的世界坐标
                float3 worldPos = _WorldSpaceCameraPos + viewPosVec;

                // 归一化到可见范围
                float3 normalized = float3(
                    worldPos.x * 0.05 + 0.5,
                    worldPos.y * 0.1,
                    worldPos.z * 0.05 + 0.5
                );

                return half4(saturate(normalized), 1);
            }
            ENDHLSL
        }
    }
}
