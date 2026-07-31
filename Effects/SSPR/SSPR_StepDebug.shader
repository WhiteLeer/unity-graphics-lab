Shader "Hidden/SSPR_StepDebug"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

    TEXTURE2D(_BaseMap);
    SAMPLER(sampler_BaseMap);
    Texture2D<uint> _SSPR_Offset;

    // SSPR参数
    float4 _SSPRParams;
    float4 _ReflectionPlane;
    float4 _SourceSize;
    float4 _ProjectionParams2;
    float4 _CameraViewTopLeftCorner;
    float4 _CameraViewXExtent;
    float4 _CameraViewYExtent;
    float4 _SSPRScreenSize; // x: width, y: height, z: 1/width, w: 1/height
    float4x4 _SSPRInvViewProj;
    float4x4 _SSPRViewProj;

    #define INTENSITY _SSPRParams.x
    #define FRESNEL_POWER _SSPRParams.y
    #define INVALID_OFFSET 0xFFFFFFFFu

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

    // Unity官方方法：重建从相机到像素的世界空间向量（和SSAO完全一致）
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

        // Pass 2: 显示到平面的距离
        Pass
        {
            Name "Step2_DistanceToPlane"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                float rawDepth = SampleSceneDepth(input.uv);
                if (rawDepth < 0.00001)
                    return half4(0, 0, 0, 1);

                float3 worldPos = ComputeWorldSpacePosition(input.uv, rawDepth, _SSPRInvViewProj);

                // 计算到平面的距离
                float distToPlane = abs(dot(worldPos, _ReflectionPlane.xyz) - _ReflectionPlane.w);

                // 归一化到0-5范围
                float vis = saturate(distToPlane / 5.0);
                return half4(vis, vis, vis, 1);
            }
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

        // Pass 6: 占位
        Pass
        {
            Name "Step6_Placeholder"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return half4(1, 1, 1, 1); }
            ENDHLSL
        }

        // Pass 7: 占位（面板左侧已经是实际GameView结果）
        Pass
        {
            Name "Step7_FinalResult"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(0, 0, 0, 1);
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

        // Pass 11: 显示法线与平面法线的点乘
        Pass
        {
            Name "Step11_NormalDot"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                float rawDepth = SampleSceneDepth(input.uv);
                if (rawDepth < 0.00001)
                    return half4(0, 0, 0, 1);

                half3 worldNormal = SampleSceneNormals(input.uv);
                float normalDot = dot(worldNormal, _ReflectionPlane.xyz);

                // 显示点乘结果
                return half4(normalDot, normalDot, normalDot, 1);
            }
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

                float3 worldPos = ComputeWorldSpacePosition(input.uv, rawDepth, _SSPRInvViewProj);

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

        // Pass 17: 显示镜像后的世界坐标
        Pass
        {
            Name "Step17_ReflectedPosition"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float3 MirrorPositionAcrossPlane(float3 worldPos, float3 planeNormal, float planeDistance)
            {
                float dist = dot(worldPos, planeNormal) - planeDistance;
                return worldPos - 2.0 * dist * planeNormal;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float rawDepth = SampleSceneDepth(input.uv);
                if (rawDepth < 0.00001)
                    return half4(0, 0, 0, 1);

                float3 worldPos = ComputeWorldSpacePosition(input.uv, rawDepth, _SSPRInvViewProj);

                // 计算镜像位置
                float3 reflectedPos = MirrorPositionAcrossPlane(worldPos, _ReflectionPlane.xyz, _ReflectionPlane.w);

                // 归一化到可见范围
                float3 normalized = float3(
                    reflectedPos.x * 0.05 + 0.5,
                    reflectedPos.y * 0.1,
                    reflectedPos.z * 0.05 + 0.5
                );

                return half4(saturate(normalized), 1);
            }
            ENDHLSL
        }

        // Pass 18: 显示投影后的裁剪空间坐标（旧镜像法）
        Pass
        {
            Name "Step18_ClipSpace"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float3 MirrorPositionAcrossPlane(float3 worldPos, float3 planeNormal, float planeDistance)
            {
                float dist = dot(worldPos, planeNormal) - planeDistance;
                return worldPos - 2.0 * dist * planeNormal;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float rawDepth = SampleSceneDepth(input.uv);
                if (rawDepth < 0.00001)
                    return half4(0, 0, 0, 1);

                float3 worldPos = ComputeWorldSpacePosition(input.uv, rawDepth, _SSPRInvViewProj);

                float3 reflectedPos = MirrorPositionAcrossPlane(worldPos, _ReflectionPlane.xyz, _ReflectionPlane.w);
                float4 reflectedCS = mul(_SSPRViewProj, float4(reflectedPos, 1.0));

                // 显示裁剪空间坐标
                float3 vis = float3(
                    reflectedCS.x / reflectedCS.w * 0.5 + 0.5,
                    reflectedCS.y / reflectedCS.w * 0.5 + 0.5,
                    reflectedCS.z / reflectedCS.w
                );

                return half4(saturate(vis), 1);
            }
            ENDHLSL
        }

        // Pass 19: 显示SSPR Offset有效性
        Pass
        {
            Name "Step19_SSPR_OffsetValid"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                uint2 pixel = uint2(input.uv * _SSPRScreenSize.xy);
                uint packed = _SSPR_Offset.Load(int3(pixel, 0));
                return (packed == INVALID_OFFSET) ? half4(1, 0, 0, 1) : half4(0, 1, 0, 1);
            }
            ENDHLSL
        }

        // Pass 20: 显示SSPR Offset映射到的UV
        Pass
        {
            Name "Step20_SSPR_OffsetUV"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                uint2 pixel = uint2(input.uv * _SSPRScreenSize.xy);
                uint packed = _SSPR_Offset.Load(int3(pixel, 0));
                if (packed == INVALID_OFFSET)
                    return half4(0, 0, 0, 1);

                uint srcX = packed & 0xFFFFu;
                uint srcY = packed >> 16;
                float2 hitUV = (float2(srcX, srcY) + 0.5) * _SSPRScreenSize.zw;
                return half4(hitUV.x, hitUV.y, 0, 1);
            }
            ENDHLSL
        }

        // Pass 21: 显示SSPR采样到的反射颜色
        Pass
        {
            Name "Step21_SSPR_SampledColor"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                uint2 pixel = uint2(input.uv * _SSPRScreenSize.xy);
                uint packed = _SSPR_Offset.Load(int3(pixel, 0));
                if (packed == INVALID_OFFSET)
                    return half4(0, 0, 0, 1);

                uint srcX = packed & 0xFFFFu;
                uint srcY = packed >> 16;
                float2 hitUV = (float2(srcX, srcY) + 0.5) * _SSPRScreenSize.zw;
                half4 reflectionColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, hitUV);
                return reflectionColor;
            }
            ENDHLSL
        }

        // Pass 22: 显示Offset覆盖区域/命中UV
        Pass
        {
            Name "Step22_SSPR_OffsetCoverage"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                uint2 pixel = uint2(input.uv * _SSPRScreenSize.xy);
                uint packed = _SSPR_Offset.Load(int3(pixel, 0));
                if (packed == INVALID_OFFSET)
                    return half4(0, 0, 0, 1);

                uint srcX = packed & 0xFFFFu;
                uint srcY = packed >> 16;
                float2 hitUV = (float2(srcX, srcY) + 0.5) * _SSPRScreenSize.zw;
                return half4(hitUV.x, hitUV.y, 0, 1);
            }
            ENDHLSL
        }
    }
}
