Shader "SurfaceLab/JadeVolume/VolumeObject"
{
    Properties
    {
        [MainColor] _BaseColor("主体颜色", Color) = (0.72, 0.86, 0.4, 1)
        _AmbientTint("环境底色", Color) = (0.08, 0.18, 0.04, 1)
        _ScatterColor("透射颜色", Color) = (0.78, 0.96, 0.78, 1)
        _SkyTint("边缘冷光", Color) = (0.55, 0.78, 0.82, 1)

        _ScatterStrength("透射强度", Range(0.0, 8.0)) = 2.2
        _ScatterDistance("透射距离", Range(0.2, 4.0)) = 2.5
        _ScatterStep("透射步长", Range(0.02, 0.5)) = 0.2
        _ScatterBlend("明暗混合", Range(0.0, 1.0)) = 0.7
        _ScatterBoost("透光提亮", Range(0.0, 8.0)) = 2.4
        _ScatterCurve("透光曲线", Range(0.2, 4.0)) = 1.2
        _ScatterIor("折射率", Range(1.01, 2.0)) = 1.12

        _FresnelPower("边缘亮度范围", Range(0.2, 8.0)) = 3.5
        _SpecularRoughness("高光柔和度", Range(0.02, 1.0)) = 0.28
        _SpecularMultiplier("高光强度", Range(0.0, 8.0)) = 1.6
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
                float _ScatterStrength;
                float _ScatterDistance;
                float _ScatterStep;
                float _ScatterBlend;
                float _ScatterBoost;
                float _ScatterCurve;
                float _ScatterIor;
                float _FresnelPower;
                float _SpecularRoughness;
                float _SpecularMultiplier;
            CBUFFER_END

            float3 _VolumeLightPositionWS;
            float4 _VolumeLightColor;
            float _VolumeLightIntensity;

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

            float JadeVolumeG1V(float dnv, float k)
            {
                return 1.0 / (dnv * (1.0 - k) + k);
            }

            float JadeVolumeGGX(float3 n, float3 v, float3 l, float rough, float f0)
            {
                float alpha = rough * rough;
                float3 h = normalize(v + l);
                float dnl = saturate(dot(n, l));
                float dnv = saturate(dot(n, v));
                float dnh = saturate(dot(n, h));
                float dlh = saturate(dot(l, h));
                float asqr = alpha * alpha;
                float den = dnh * dnh * (asqr - 1.0) + 1.0;
                float d = asqr / (PI * den * den);
                float f = f0 + (1.0 - f0) * pow(1.0 - dlh, 5.0);
                float vis = JadeVolumeG1V(dnl, alpha) * JadeVolumeG1V(dnv, alpha);
                return dnl * d * f * vis;
            }

            float JadeVolumeSimpleTransmission(float3 n, float3 v)
            {
                float ndv = saturate(dot(n, v));
                float thickness = pow(1.0 - ndv, max(_ScatterIor - 1.0, 0.1));
                float softened = thickness * _ScatterDistance;
                float stepped = softened / max(_ScatterStep, 0.001);
                float transmission = (1.0 - exp(-stepped * max(_ScatterStrength, 1e-3))) * _ScatterBoost;
                return pow(saturate(transmission), max(_ScatterCurve, 1e-3));
            }

            half4 Frag(Varyings i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                float3 v = normalize(i.viewDirWS);
                float faceSign = isFrontFace ? 1.0 : -1.0;
                float3 n = normalize(i.normalWS) * faceSign;
                float3 l = normalize(_VolumeLightPositionWS - i.positionWS);
                float transmission = JadeVolumeSimpleTransmission(n, v);

                float nDotL = saturate(dot(n, l));
                float fresnel = pow(1.0 - saturate(dot(n, v)), _FresnelPower);
                float spec = JadeVolumeGGX(n, v, l, _SpecularRoughness, fresnel) * _SpecularMultiplier;

                float lightDistance = max(distance(_VolumeLightPositionWS, i.positionWS), 1e-3);
                float attenuation = _VolumeLightIntensity / (1.0 + lightDistance * lightDistance);
                float3 lightColor = _VolumeLightColor.rgb * attenuation;

                float3 color = _AmbientTint.rgb * 0.18;
                color += _BaseColor.rgb * (0.22 + 0.78 * nDotL);
                color += _ScatterColor.rgb * transmission * _ScatterBlend;
                color += _SkyTint.rgb * fresnel;
                color += spec * lightColor;
                color *= lightColor;
                color += _AmbientTint.rgb * 0.06;

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
