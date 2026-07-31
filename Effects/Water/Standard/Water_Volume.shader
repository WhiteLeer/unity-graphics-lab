Shader "SurfaceLab/Water/Volume"
{
    Properties
    {
        [HDR] _ShallowColor("浅水颜色", Color) = (0.08, 0.55, 0.72, 1)
        [HDR] _DeepColor("深水颜色", Color) = (0.02, 0.22, 0.38, 1)
        [HDR] _EnvironmentColor("环境反射颜色", Color) = (0.2, 0.5, 0.8, 1)
        _EnvironmentTex("环境贴图", 2D) = "white" {}
        _EnvironmentStrength("环境反射强度", Range(0, 4)) = 1.2
        _EnvironmentRefractionStrength("环境折射强度", Range(0, 2)) = 0.9
        _ScatterStrength("水体散射强度", Range(0, 4)) = 1.8
        [HDR] _SubsurfaceColor("次表面颜色", Color) = (0.18, 0.75, 0.82, 1)
        _SubsurfaceStrength("次表面强度", Range(0, 4)) = 0.9
        _SpecularStrength("高光强度", Range(0, 4)) = 1.3
        _WaterLightDirection("水体主光方向", Vector) = (-1, 1, -2, 0)
        _Opacity("透明度", Range(0, 1)) = 0.78
        _Smoothness("光滑度", Range(0, 1)) = 0.96
        _IOR("折射率", Range(1.001, 1.5)) = 1.333
        _FresnelPower("菲涅尔范围", Range(0.5, 8)) = 5
        _RefractionStrength("折射强度", Range(0, 0.08)) = 0.025
        _AbsorptionDensity("吸收密度", Range(0, 8)) = 1.4
        _ThicknessScale("厚度倍率", Range(0.05, 4)) = 1
        _FallbackThickness("厚度回退值", Range(0.01, 8)) = 1
        _NormalFrequency("波纹频率", Range(0.05, 16)) = 2.5
        _NormalStrength("波纹强度", Range(0, 1)) = 0.12
        _WaveSpeed("波纹速度", Range(0, 5)) = 0.65
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _ShallowColor;
            float4 _DeepColor;
            float4 _EnvironmentColor;
            float _EnvironmentStrength;
            float _EnvironmentRefractionStrength;
            float _ScatterStrength;
            float4 _SubsurfaceColor;
            float _SubsurfaceStrength;
            float _SpecularStrength;
            float4 _WaterLightDirection;
            float _Opacity;
            float _Smoothness;
            float _IOR;
            float _FresnelPower;
            float _RefractionStrength;
            float _AbsorptionDensity;
            float _ThicknessScale;
            float _FallbackThickness;
            float _NormalFrequency;
            float _NormalStrength;
            float _WaveSpeed;
            float4 _VolumeBoundsScale;
        CBUFFER_END

        TEXTURE2D(_EnvironmentTex);
        SAMPLER(sampler_EnvironmentTex);

        float3 _VolumeLightPositionWS;
        float4 _VolumeLightColor;
        float _VolumeLightIntensity;

        float3 WaterSampleEnvironment(float3 direction)
        {
            direction = normalize(direction);
            float2 uv = float2(
                atan2(direction.z, direction.x) * (0.5 / 3.14159265) + 0.5,
                asin(clamp(direction.y, -0.999, 0.999)) / 3.14159265 + 0.5);
            return SAMPLE_TEXTURE2D_LOD(_EnvironmentTex, sampler_EnvironmentTex, uv, 0).rgb;
        }
        ENDHLSL

        Pass
        {
            Name "WaterThicknessBackface"
            Tags { "LightMode" = "WaterThicknessBackface" }
            Cull Front
            ZWrite On
            ZTest LEqual
            ColorMask R

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex BackfaceVert
            #pragma fragment BackfaceFrag

            struct BackfaceAttributes
            {
                float4 positionOS : POSITION;
            };

            struct BackfaceVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            BackfaceVaryings BackfaceVert(BackfaceAttributes input)
            {
                BackfaceVaryings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                return output;
            }

            float BackfaceFrag(BackfaceVaryings input) : SV_Target
            {
                return max(-TransformWorldToView(input.positionWS).z, 0.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "WaterVolume"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "WaterCommon.hlsl"

            TEXTURE2D_X_FLOAT(_VolumeBackfaceDepthTexture);
            SAMPLER(sampler_VolumeBackfaceDepthTexture);
            float _VolumeThicknessAvailable;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 waveNormalWS : TEXCOORD2;
                float4 screenPosition : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                float3 baseNormalWS = TransformObjectToWorldNormal(input.normalOS);
                float waveHeight;
                float2 waveGradient;
                WaterEvaluateWaves(
                    positionInputs.positionWS.xz,
                    _Time.y * _WaveSpeed,
                    _NormalFrequency,
                    1.0,
                    1.0,
                    _WaterLightDirection.xy,
                    waveHeight,
                    waveGradient);
                float3 waveDetailWS = float3(-waveGradient.x, 0.0, -waveGradient.y);
                waveDetailWS -= baseNormalWS * dot(waveDetailWS, baseNormalWS);
                output.waveNormalWS = SafeNormalize(baseNormalWS + waveDetailWS * (_NormalStrength * 0.32));
                output.normalWS = output.waveNormalWS;
                output.screenPosition = ComputeScreenPos(positionInputs.positionCS);
                return output;
            }

            float ResolveThickness(Varyings input, float2 screenUV)
            {
                float frontDepth = max(-TransformWorldToView(input.positionWS).z, 0.0);
                float backDepth = SAMPLE_TEXTURE2D_X(
                    _VolumeBackfaceDepthTexture,
                    sampler_VolumeBackfaceDepthTexture,
                    screenUV).r;
                float measuredThickness = max(backDepth - frontDepth, 0.0);
                float validMeasurement = saturate(_VolumeThicknessAvailable) * step(1e-4, measuredThickness);
                float boundsScale = max(_VolumeBoundsScale.x, max(_VolumeBoundsScale.y, _VolumeBoundsScale.z));
                float fallback = max(_FallbackThickness * max(boundsScale, 1e-3), 1e-3);
                return lerp(fallback, measuredThickness, validMeasurement) * _ThicknessScale;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 baseNormalWS = normalize(input.normalWS);
                float3 lowFrequencyNormalWS = normalize(input.waveNormalWS);
                float3 normalWS = WaterPerturbNormal(
                    input.positionWS,
                    baseNormalWS,
                    _Time.y * _WaveSpeed,
                    _NormalFrequency,
                    _NormalStrength);
                float3 viewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                float noV = saturate(dot(normalWS, viewDirectionWS));
                float fresnel = WaterFresnel(noV, _IOR, _FresnelPower);

                float2 screenUV = input.screenPosition.xy / max(input.screenPosition.w, 1e-5);
                float thickness = ResolveThickness(input, screenUV);
                float depthFactor = 1.0 - exp(-thickness * max(_AbsorptionDensity, 0.0));
                float3 waterColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(depthFactor));
                float3 transmittance = WaterTransmittance(_ShallowColor.rgb, _AbsorptionDensity, thickness);
                float2 refractedUV = WaterDistortedScreenUV(input.screenPosition, normalWS, _RefractionStrength);
                float3 sceneColor = SampleSceneColor(refractedUV);
                float opaqueDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfaceDepth = max(-TransformWorldToView(input.positionWS).z, 0.0);
                float hasOpaqueBackground = WaterHasOpaqueBackground(opaqueDepth, surfaceDepth);
                float3 refractedDirection = refract(-viewDirectionWS, normalWS, rcp(max(_IOR, 1.001)));
                float3 fallbackDirection = reflect(-viewDirectionWS, normalWS);
                float refractionValid = step(1e-4, dot(refractedDirection, refractedDirection));
                refractedDirection = normalize(lerp(fallbackDirection, refractedDirection, refractionValid));
                float3 environmentRefraction = WaterSampleEnvironment(refractedDirection) * _EnvironmentRefractionStrength;
                float3 refractionSource = lerp(environmentRefraction, sceneColor, hasOpaqueBackground);
                float3 refraction = refractionSource * transmittance + waterColor * (1.0 - transmittance);

                float3 reflectionDirection = reflect(-viewDirectionWS, normalWS);
                float3 probeReflection = GlossyEnvironmentReflection(reflectionDirection, 1.0 - _Smoothness, 1.0);
                float skyGradient = lerp(0.35, 1.0, saturate(reflectionDirection.y * 0.5 + 0.5));
                float3 environmentFallback = _EnvironmentColor.rgb * (_EnvironmentStrength * skyGradient);
                float3 texturedEnvironment = WaterSampleEnvironment(reflectionDirection) * _EnvironmentStrength;
                float probeWeight = smoothstep(0.002, 0.08, dot(probeReflection, float3(0.2126, 0.7152, 0.0722)));
                float3 reflection = lerp(environmentFallback, texturedEnvironment, 0.72);
                reflection = lerp(reflection, probeReflection, probeWeight * 0.35);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float mainAttenuation = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                float mainNoL = saturate(dot(normalWS, mainLight.direction));
                float mainSpecular = WaterGGXSpecular(
                    normalWS,
                    viewDirectionWS,
                    mainLight.direction,
                    _Smoothness,
                    _IOR) * mainNoL * mainAttenuation;

                float3 pointVector = _VolumeLightPositionWS - input.positionWS;
                float pointDistance = max(length(pointVector), 1e-3);
                float3 pointDirection = pointVector / pointDistance;
                float pointAttenuation = max(_VolumeLightIntensity, 0.0) / (1.0 + pointDistance * pointDistance);
                float3 pointLightColor = _VolumeLightColor.rgb * pointAttenuation;
                float pointNoL = saturate(dot(normalWS, pointDirection));
                float pointBackLight = saturate(dot(-normalWS, pointDirection));
                float pointSpecular = WaterGGXSpecular(
                    normalWS,
                    viewDirectionWS,
                    pointDirection,
                    _Smoothness,
                    _IOR) * pointNoL;

                float3 referenceLightDirection = SafeNormalize(_WaterLightDirection.xyz);
                float referenceFrontLight = saturate(dot(normalWS, referenceLightDirection));
                float referenceBackLight = saturate(dot(-normalWS, referenceLightDirection));
                float3 directLight = mainLight.color * (mainNoL * mainAttenuation) + pointLightColor * (pointNoL + pointBackLight * depthFactor);
                float3 bodyColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(depthFactor * 0.7));
                float backScatter = (pointBackLight + referenceBackLight * 0.5) * depthFactor;
                float absorbedEnergy = saturate(1.0 - dot(transmittance, float3(0.2126, 0.7152, 0.0722)));
                float scatterDepth = saturate(depthFactor * 0.65 + absorbedEnergy * 0.55);
                float3 scatter = bodyColor * (directLight + referenceFrontLight * 0.15 + backScatter) * (_ScatterStrength * scatterDepth);
                float3 subsurface = WaterSubsurface(
                    viewDirectionWS,
                    lowFrequencyNormalWS,
                    referenceLightDirection,
                    fresnel,
                    _SubsurfaceStrength,
                    _SubsurfaceColor.rgb);
                subsurface *= lerp(0.45, 1.0, scatterDepth);
                float3 specularColor = (mainLight.color * mainSpecular + pointLightColor * pointSpecular) * _SpecularStrength;

                float3 color = lerp(refraction, reflection, fresnel);
                color += scatter * (1.0 - fresnel);
                color += subsurface;
                color += specularColor;
                float alpha = saturate(_Opacity + depthFactor * 0.22 + fresnel * 0.18);
                return float4(color, alpha);
            }
            ENDHLSL
        }
    }
}
