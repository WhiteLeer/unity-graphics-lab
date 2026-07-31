Shader "SurfaceLab/JadeVolume/VolumeObject"
{
    Properties
    {
        [MainColor] _BaseColor("主体颜色", Color) = (0.75, 0.9, 0.35, 1)
        _AmbientTint("环境底色", Color) = (0.0, 0.0, 0.0, 1)
        _SkyTint("边缘冷光", Color) = (0.368, 0.559, 0.83, 1)

        _ScatterStrength("透射强度", Range(0.0, 64.0)) = 21.2
        _ScatterDistance("透射距离", Range(0.2, 8.0)) = 1.34
        _ScatterStep("厚度归一化步长", Range(0.02, 0.5)) = 0.179
        _ScatterBlend("明暗混合", Range(0.0, 1.0)) = 0.692
        _ScatterBoost("透光提亮", Range(0.0, 8.0)) = 3.0
        _ScatterCurve("透光曲线", Range(0.1, 2.0)) = 0.45
        _ScatterIor("折射率", Range(1.01, 2.0)) = 1.121

        _FresnelPower("边缘亮度范围", Range(0.2, 8.0)) = 4.42
        _SpecularRoughness("高光柔和度", Range(0.02, 1.0)) = 0.1
        _SpecularMultiplier("高光强度", Range(0.0, 64.0)) = 17.7
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
            Name "JadeThicknessBackface"
            Tags
            {
                "LightMode" = "JadeThicknessBackface"
            }
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ThicknessVert
            #pragma fragment ThicknessFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct ThicknessAttributes
            {
                float4 positionOS : POSITION;
            };

            struct ThicknessVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            ThicknessVaryings ThicknessVert(ThicknessAttributes input)
            {
                ThicknessVaryings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                return output;
            }

            float ThicknessFrag(ThicknessVaryings input) : SV_Target
            {
                return -TransformWorldToView(input.positionWS).z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "JadeVolumeObject"
            Tags
            {
                "LightMode" = "UniversalForward"
            }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _AmbientTint;
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
            float _JadeThicknessAvailable;

            TEXTURE2D_X_FLOAT(_JadeBackfaceDepthTexture);
            SAMPLER(sampler_JadeBackfaceDepthTexture);

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
                return 1.0 / max(dnv * (1.0 - k) + k, 1e-4);
            }

            float3 JadeVolumeSafeNormalize(float3 value, float3 fallback)
            {
                float lengthSquared = dot(value, value);
                return lengthSquared > 1e-8
                    ? value * rsqrt(lengthSquared)
                    : fallback;
            }

            float JadeVolumeGGX(float3 n, float3 v, float3 l, float rough, float f0)
            {
                float alpha = rough * rough;
                float3 h = JadeVolumeSafeNormalize(v + l, n);
                float dnl = saturate(dot(n, l));
                float dnv = saturate(dot(n, v));
                float dnh = saturate(dot(n, h));
                float dlh = saturate(dot(l, h));
                float asqr = alpha * alpha;
                float den = max(dnh * dnh * (asqr - 1.0) + 1.0, 1e-4);
                float d = asqr / (PI * den * den);
                float f = f0 + (1.0 - f0) * pow(1.0 - dlh, 5.0);
                float vis = JadeVolumeG1V(dnl, alpha) * JadeVolumeG1V(dnv, alpha);
                return dnl * d * f * vis;
            }

            float JadeVolumeMeshThickness(Varyings input, float3 n, float3 v)
            {
                float ndv = saturate(dot(n, v));
                float fallbackThickness = max(_ScatterDistance * ndv, _ScatterStep);

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                screenUV = UnityStereoTransformScreenSpaceTex(screenUV);
                float backfaceDepth = SAMPLE_TEXTURE2D_X(
                    _JadeBackfaceDepthTexture,
                    sampler_JadeBackfaceDepthTexture,
                    screenUV).r;
                float frontfaceDepth = -TransformWorldToView(input.positionWS).z;
                float geometricThickness = max(backfaceDepth - frontfaceDepth, 0.0);
                float validBackface = step(1e-4, geometricThickness);
                float thickness = lerp(
                    fallbackThickness,
                    geometricThickness,
                    saturate(_JadeThicknessAvailable) * validBackface);

                float3 incidentDirection = -v;
                float3 refractedDirection = refract(
                    incidentDirection,
                    n,
                    rcp(max(_ScatterIor, 1.001)));
                refractedDirection = JadeVolumeSafeNormalize(refractedDirection, -n);

                // Convert camera-ray thickness to a locally planar refracted optical path.
                float normalThickness = thickness * max(ndv, 0.02);
                return normalThickness / max(abs(dot(refractedDirection, n)), 0.02);
            }

            float JadeVolumeReferenceTransmission(float opticalThickness)
            {
                float accumulatedThickness = opticalThickness / max(_ScatterStep, 0.001);
                float subsurface = _ScatterStrength * pow(_ScatterDistance * 0.5, 3.0) /
                                   max(accumulatedThickness, 1e-4);
                return _ScatterBoost * smoothstep(
                    0.0,
                    2.0,
                    pow(max(subsurface, 0.0), max(_ScatterCurve, 1e-3)));
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 v = JadeVolumeSafeNormalize(i.viewDirWS, float3(0.0, 0.0, 1.0));
                float3 n = JadeVolumeSafeNormalize(i.normalWS, float3(0.0, 1.0, 0.0));
                float3 l = JadeVolumeSafeNormalize(_VolumeLightPositionWS - i.positionWS, n);
                float opticalThickness = JadeVolumeMeshThickness(i, n, v);
                float transmission = JadeVolumeReferenceTransmission(opticalThickness);

                float nDotL = saturate(dot(n, l));
                float fresnel = pow(1.0 - saturate(dot(n, v)), _FresnelPower);
                float spec = JadeVolumeGGX(n, v, l, _SpecularRoughness, fresnel) * _SpecularMultiplier;

                float lightDistance = max(distance(_VolumeLightPositionWS, i.positionWS), 1e-3);
                float attenuation = _VolumeLightIntensity / (1.0 + lightDistance * lightDistance);
                float3 lightColor = _VolumeLightColor.rgb * attenuation;

                float jadeLighting = lerp(nDotL, transmission, _ScatterBlend);
                float3 color = _AmbientTint.rgb;
                color += _BaseColor.rgb * jadeLighting;
                color += spec * lightColor;
                color += fresnel * _SkyTint.rgb * 2.0;
                color *= 0.5;

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
