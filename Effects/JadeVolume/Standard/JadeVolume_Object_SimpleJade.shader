Shader "SurfaceLab/JadeVolume/VolumeObjectSimpleJade"
{
    Properties
    {
        [MainColor] _BaseColor("主体颜色", Color) = (0.72, 0.86, 0.4, 1)
        _AmbientTint("环境底色", Color) = (0.08, 0.18, 0.04, 1)
        _ScatterColor("透射颜色", Color) = (0.78, 0.96, 0.78, 1)
        _SkyTint("边缘冷光", Color) = (0.55, 0.78, 0.82, 1)
        _DensityTex("三维噪声图", 3D) = "" {}

        _NoiseFrequency("噪声频率", Range(0.25, 8.0)) = 2.2
        _NoiseAmount("噪声扰动", Range(0.0, 0.25)) = 0.08
        _ShapeMode("形状模式", Range(0, 3)) = 0
        _ShapeBlend("形体圆润", Range(0.0, 0.2)) = 0.04
        _SurfaceOffset("表面偏移", Range(-0.1, 0.1)) = 0.0

        _ThicknessSampleCount("厚度采样次数", Range(4, 64)) = 32
        _ThicknessSampleDepth("厚度采样深度", Range(0.05, 2.0)) = 1.0
        _SSSAmbient("基础透光", Range(0.01, 1.0)) = 0.18
        _SSSDistortion("透光偏折", Range(0.01, 2.0)) = 0.55
        _SSSPower("透光集中", Range(0.01, 2.0)) = 0.75
        _SSSScale("透光强度", Range(0.01, 5.0)) = 1.45

        _FresnelPower("边缘亮度范围", Range(0.2, 8.0)) = 3.5
        _SpecularRoughness("高光柔和度", Range(0.02, 1.0)) = 0.28
        _SpecularMultiplier("高光强度", Range(0.0, 8.0)) = 1.6

        _TraceSteps("主追踪步数", Range(24, 256)) = 128
        _HitEpsilon("命中精度", Range(0.001, 0.03)) = 0.004
        _MaxDistance("最大距离", Range(0.5, 4.0)) = 2.0
        _NormalStep("法线精度", Range(0.001, 0.03)) = 0.01
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "JadeVolumeObject"
            Tags
            {
                "LightMode" = "UniversalForward"
            }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _AmbientTint;
                float4 _ScatterColor;
                float4 _SkyTint;
                float _NoiseFrequency;
                float _NoiseAmount;
                float _ShapeMode;
                float _ShapeBlend;
                float _SurfaceOffset;
                float _ThicknessSampleCount;
                float _ThicknessSampleDepth;
                float _SSSAmbient;
                float _SSSDistortion;
                float _SSSPower;
                float _SSSScale;
                float _FresnelPower;
                float _SpecularRoughness;
                float _SpecularMultiplier;
                float _TraceSteps;
                float _HitEpsilon;
                float _MaxDistance;
                float _NormalStep;
            CBUFFER_END

            TEXTURE3D(_DensityTex);
            SAMPLER(sampler_DensityTex);

            float3 _VolumeLightPositionWS;
            float4 _VolumeLightColor;
            float _VolumeLightIntensity;

            #include "JadeVolumeMeshSurfaceCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 viewDirWS : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.positionOS = input.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.viewDirWS = GetWorldSpaceViewDir(pos.positionWS);
                return o;
            }

            half4 Frag(Varyings i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                float3 v = normalize(i.viewDirWS);
                float faceSign = isFrontFace ? 1.0 : -1.0;
                float3 n = normalize(i.normalWS) * faceSign;
                float3 l = normalize(_VolumeLightPositionWS - i.positionWS);
                float3 vOS = normalize(TransformWorldToObjectDir(v));

                float surfaceNoise = JadeVolumeSampleNoise(i.positionOS * 0.85 + n * 0.12);
                float transmission = JadeVolumeTransmission(
                    i.positionOS,
                    n,
                    vOS,
                    _ThicknessSampleDepth,
                    max(_NormalStep, 0.03),
                    0.85,
                    _SSSScale,
                    _SSSPower,
                    1.3);
                transmission *= lerp(0.85, 1.15, surfaceNoise);

                float nDotL = saturate(dot(n, l));
                float fresnel = pow(1.0 - saturate(dot(n, v)), _FresnelPower);
                float spec = JadeVolumeGGX(n, v, l, _SpecularRoughness, fresnel) * _SpecularMultiplier;

                float lightDistance = max(distance(_VolumeLightPositionWS, i.positionWS), 1e-3);
                float attenuation = _VolumeLightIntensity / (1.0 + lightDistance * lightDistance);
                float3 lightColor = _VolumeLightColor.rgb * attenuation;

                float3 color = _AmbientTint.rgb * 0.18;
                color += _BaseColor.rgb * (0.24 + 0.76 * nDotL);
                color += _ScatterColor.rgb * transmission * _SSSAmbient;
                color += _SkyTint.rgb * fresnel * (0.55 + 0.25 * surfaceNoise);
                color += spec * lightColor;
                color *= lightColor;
                color += _AmbientTint.rgb * (0.06 + 0.10 * surfaceNoise);

                return float4(saturate(color), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LightMode" = "DepthOnly"
            }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Assets/unity-shadertoy-validation/Common/Shaders/ShadertoyDepthOnlyPass.hlsl"
            ENDHLSL
        }
    }

    CustomEditor "JadeVolumeShaderGUI"
}
