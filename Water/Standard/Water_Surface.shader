Shader "SurfaceLab/Water/Surface"
{
    Properties
    {
        [HDR] _ShallowColor("浅水颜色", Color) = (0.08, 0.55, 0.72, 1)
        [HDR] _DeepColor("深水颜色", Color) = (0.02, 0.22, 0.38, 1)
        [HDR] _FoamColor("泡沫颜色", Color) = (0.8, 0.95, 1, 1)
        [HDR] _EnvironmentColor("环境反射颜色", Color) = (0.2, 0.5, 0.8, 1)
        _EnvironmentTex("环境贴图", 2D) = "white" {}
        _EnvironmentStrength("环境反射强度", Range(0, 4)) = 0.75
        _EnvironmentRefractionStrength("环境折射强度", Range(0, 2)) = 0.8
        _ScatterStrength("水体散射强度", Range(0, 4)) = 0.75
        [HDR] _SubsurfaceColor("次表面颜色", Color) = (0.18, 0.75, 0.82, 1)
        _SubsurfaceStrength("次表面强度", Range(0, 4)) = 0.65
        _CausticStrength("焦散强度", Range(0, 4)) = 0.8
        _CausticScale("焦散尺度", Range(0.25, 8)) = 2
        _CausticSpeed("焦散速度", Range(0, 2)) = 0.15
        _CausticFallback("无接收面焦散回退", Range(0, 1)) = 0.35
        _SpecularStrength("高光强度", Range(0, 4)) = 1.2
        _WaterLightDirection("水面主光方向", Vector) = (-1, 1, -2, 0)
        _Opacity("透明度", Range(0, 1)) = 0.78
        _Smoothness("光滑度", Range(0, 1)) = 0.94
        _IOR("折射率", Range(1.001, 1.5)) = 1.333
        _FresnelPower("菲涅尔范围", Range(0.5, 8)) = 5
        _RefractionStrength("折射强度", Range(0, 0.08)) = 0.018
        _AbsorptionDensity("吸收密度", Range(0, 8)) = 1.2
        _DepthDistance("深水距离", Range(0.05, 20)) = 3
        _WaveAmplitude("波浪高度", Range(0, 1)) = 0.12
        _WaveFrequency("波浪频率", Range(0.05, 8)) = 1.2
        _WaveSpeed("波浪速度", Range(0, 5)) = 0.8
        _WaveDirection("波浪方向", Vector) = (1, 0.35, 0, 0)
        _NormalStrength("细节法线", Range(0, 1)) = 0.18
        _FoamDistance("岸边泡沫范围", Range(0.01, 2)) = 0.18
        _FoamStrength("岸边泡沫强度", Range(0, 2)) = 0.75
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "WaterSurface"
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "WaterCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float4 _FoamColor;
                float4 _EnvironmentColor;
                float _EnvironmentStrength;
                float _EnvironmentRefractionStrength;
                float _ScatterStrength;
                float4 _SubsurfaceColor;
                float _SubsurfaceStrength;
                float _CausticStrength;
                float _CausticScale;
                float _CausticSpeed;
                float _CausticFallback;
                float _SpecularStrength;
                float4 _WaterLightDirection;
                float _Opacity;
                float _Smoothness;
                float _IOR;
                float _FresnelPower;
                float _RefractionStrength;
                float _AbsorptionDensity;
                float _DepthDistance;
                float _WaveAmplitude;
                float _WaveFrequency;
                float _WaveSpeed;
                float4 _WaveDirection;
                float _NormalStrength;
                float _FoamDistance;
            float _FoamStrength;
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

            float4 WaterSurfaceMod289(float4 value)
            {
                return value - floor(value / 289.0) * 289.0;
            }

            float4 WaterSurfacePermute(float4 value)
            {
                return WaterSurfaceMod289((value * 34.0 + 1.0) * value);
            }

            float4 WaterSurfaceSimplexNoise(float3 value)
            {
                const float2 simplex = float2(1.0 / 6.0, 1.0 / 3.0);
                float3 cell = floor(value + dot(value, simplex.yyy));
                float3 offset0 = value - cell + dot(cell, simplex.xxx);
                float3 greater = step(offset0.yzx, offset0.xyz);
                float3 lesser = 1.0 - greater;
                float3 offset1Index = min(greater.xyz, lesser.zxy);
                float3 offset2Index = max(greater.xyz, lesser.zxy);
                float3 offset1 = offset0 - offset1Index + simplex.x;
                float3 offset2 = offset0 - offset2Index + simplex.y;
                float3 offset3 = offset0 - 0.5;
                float4 permutation = WaterSurfacePermute(
                    WaterSurfacePermute(WaterSurfacePermute(cell.z + float4(0.0, offset1Index.z, offset2Index.z, 1.0)) + cell.y +
                    float4(0.0, offset1Index.y, offset2Index.y, 1.0)) + cell.x +
                    float4(0.0, offset1Index.x, offset2Index.x, 1.0));
                float4 gradientIndex = permutation - 49.0 * floor(permutation / 49.0);
                float4 gradientX = floor(gradientIndex / 7.0);
                float4 gradientY = floor(gradientIndex - 7.0 * gradientX);
                float4 x = (gradientX * 2.0 + 0.5) / 7.0 - 1.0;
                float4 y = (gradientY * 2.0 + 0.5) / 7.0 - 1.0;
                float4 height = 1.0 - abs(x) - abs(y);
                float4 basis0 = float4(x.xy, y.xy);
                float4 basis1 = float4(x.zw, y.zw);
                float4 sign0 = floor(basis0) * 2.0 + 1.0;
                float4 sign1 = floor(basis1) * 2.0 + 1.0;
                float4 shift = -step(height, 0.0);
                float4 angle0 = basis0.xzyw + sign0.xzyw * shift.xxyy;
                float4 angle1 = basis1.xzyw + sign1.xzyw * shift.zzww;
                float3 gradient0 = float3(angle0.xy, height.x);
                float3 gradient1 = float3(angle0.zw, height.y);
                float3 gradient2 = float3(angle1.xy, height.z);
                float3 gradient3 = float3(angle1.zw, height.w);
                float4 falloff = max(0.6 - float4(dot(offset0, offset0), dot(offset1, offset1), dot(offset2, offset2), dot(offset3, offset3)), 0.0);
                float4 falloff2 = falloff * falloff;
                float4 falloff3 = falloff2 * falloff;
                float4 falloff4 = falloff2 * falloff2;
                float3 gradient = -6.0 * falloff3.x * offset0 * dot(offset0, gradient0) + falloff4.x * gradient0;
                gradient += -6.0 * falloff3.y * offset1 * dot(offset1, gradient1) + falloff4.y * gradient1;
                gradient += -6.0 * falloff3.z * offset2 * dot(offset2, gradient2) + falloff4.z * gradient2;
                gradient += -6.0 * falloff3.w * offset3 * dot(offset3, gradient3) + falloff4.w * gradient3;
                float4 projection = float4(dot(offset0, gradient0), dot(offset1, gradient1), dot(offset2, gradient2), dot(offset3, gradient3));
                return float4(gradient, dot(falloff4, projection));
            }

            float WaterSurfaceCausticField(float3 positionWS)
            {
                float4 noise = WaterSurfaceSimplexNoise(positionWS);
                positionWS -= 0.07 * noise.xyz;
                positionWS *= 1.62;
                noise = WaterSurfaceSimplexNoise(positionWS);
                positionWS -= 0.07 * noise.xyz;
                noise = WaterSurfaceSimplexNoise(positionWS);
                positionWS -= 0.07 * noise.xyz;
                noise = WaterSurfaceSimplexNoise(positionWS);
                return noise.w;
            }

            float3 WaterSurfaceCaustics(float3 positionWS, float alpha)
            {
                float3 offset = float3(0.02, 0.0, 0.02);
                float3 caustics;
                caustics.x = WaterSurfaceCausticField(positionWS + offset);
                caustics.y = WaterSurfaceCausticField(positionWS + offset * 4.0);
                caustics.z = WaterSurfaceCausticField(positionWS + offset * 6.0);
                return exp(caustics * 4.0 - 1.0) * alpha;
            }

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
                float4 screenPosition : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float height;
                float2 gradient;
                WaterEvaluateWaves(
                    input.positionOS.xz,
                    _Time.y,
                    _WaveFrequency,
                    _WaveAmplitude,
                    _WaveSpeed,
                    _WaveDirection.xy,
                    height,
                    gradient);

                float3 positionOS = input.positionOS.xyz;
                positionOS.y += height;
                float3 waveNormalOS = normalize(float3(-gradient.x, 1.0, -gradient.y));
                VertexPositionInputs positionInputs = GetVertexPositionInputs(positionOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(waveNormalOS);
                output.screenPosition = ComputeScreenPos(positionInputs.positionCS);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 lowFrequencyNormalWS = normalize(input.normalWS);
                float3 normalWS = WaterPerturbNormal(
                    input.positionWS,
                    lowFrequencyNormalWS,
                    _Time.y * _WaveSpeed,
                    _WaveFrequency * 2.0,
                    _NormalStrength);
                float3 viewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                float noV = saturate(dot(normalWS, viewDirectionWS));

                float2 screenUV = input.screenPosition.xy / max(input.screenPosition.w, 1e-5);
                float2 refractedUV = WaterDistortedScreenUV(input.screenPosition, normalWS, _RefractionStrength);
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfaceDepth = -TransformWorldToView(input.positionWS).z;
                float hasOpaqueBackground = WaterHasOpaqueBackground(sceneDepth, surfaceDepth);
                float measuredDepth = max(sceneDepth - surfaceDepth, 0.0);
                float grazing = 1.0 - noV;
                float fallbackDepth = max(_DepthDistance * lerp(0.03, 0.12, grazing), 0.03);
                float waterDepth = lerp(fallbackDepth, measuredDepth, hasOpaqueBackground);
                float depthFactor = saturate(waterDepth / max(_DepthDistance, 1e-4));

                float3 waterColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthFactor);
                float3 transmittance = WaterTransmittance(_ShallowColor.rgb, _AbsorptionDensity, waterDepth);
                float3 sceneColor = SampleSceneColor(refractedUV);
                float3 refractedDirection = refract(-viewDirectionWS, normalWS, rcp(max(_IOR, 1.001)));
                float3 fallbackDirection = reflect(-viewDirectionWS, normalWS);
                float refractionValid = step(1e-4, dot(refractedDirection, refractedDirection));
                refractedDirection = normalize(lerp(fallbackDirection, refractedDirection, refractionValid));
                float3 environmentRefraction = WaterSampleEnvironment(refractedDirection) * _EnvironmentRefractionStrength;
                float3 refractionSource = lerp(environmentRefraction, sceneColor, hasOpaqueBackground);
                float3 refraction = refractionSource * transmittance + waterColor * (1.0 - transmittance);
                float3 causticPositionWS = input.positionWS + refractedDirection * max(waterDepth, 0.05);
                float causticReceiver = max(hasOpaqueBackground, _CausticFallback);
                float causticAlpha = causticReceiver * (1.0 - exp(-waterDepth * 2.0));
                float3 caustics = WaterSurfaceCaustics(
                    causticPositionWS * _CausticScale + float3(0.0, _Time.y * _CausticSpeed, 0.0),
                    causticAlpha);

                float fresnel = WaterFresnel(noV, _IOR, _FresnelPower);
                float3 reflectionDirection = reflect(-viewDirectionWS, normalWS);
                float perceptualRoughness = 1.0 - _Smoothness;
                float3 probeReflection = GlossyEnvironmentReflection(reflectionDirection, perceptualRoughness, 1.0);
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
                float pointSpecular = WaterGGXSpecular(
                    normalWS,
                    viewDirectionWS,
                    pointDirection,
                    _Smoothness,
                    _IOR) * pointNoL;

                float3 referenceLightDirection = SafeNormalize(_WaterLightDirection.xyz);
                float referenceNoL = saturate(dot(normalWS, referenceLightDirection));
                float3 referenceSpecularDirection = SafeNormalize(float3(
                    -referenceLightDirection.x,
                    abs(referenceLightDirection.y),
                    -referenceLightDirection.z));
                float referenceSpecular = WaterGGXSpecular(
                    normalWS,
                    viewDirectionWS,
                    referenceSpecularDirection,
                    _Smoothness,
                    _IOR) * saturate(dot(normalWS, referenceSpecularDirection));
                float3 diffuseLight = mainLight.color * (mainNoL * mainAttenuation) + pointLightColor * pointNoL;
                float absorbedEnergy = saturate(1.0 - dot(transmittance, float3(0.2126, 0.7152, 0.0722)));
                float scatterDepth = saturate(depthFactor * 0.75 + absorbedEnergy * 0.5);
                float3 scatter = waterColor * (diffuseLight + referenceNoL * 0.2) * (_ScatterStrength * scatterDepth);
                float3 subsurface = WaterSubsurface(
                    viewDirectionWS,
                    lowFrequencyNormalWS,
                    referenceLightDirection,
                    fresnel,
                    _SubsurfaceStrength,
                    _SubsurfaceColor.rgb);
                subsurface *= lerp(0.35, 1.0, scatterDepth);
                float3 specularColor = (mainLight.color * mainSpecular + pointLightColor * pointSpecular) * _SpecularStrength;
                specularColor += referenceSpecular.xxx * (_SpecularStrength * 0.75);
                float3 volumeColor = waterColor * (waterDepth * exp(-waterDepth * _AbsorptionDensity)) * 0.3;

                float foam = hasOpaqueBackground * (1.0 - smoothstep(0.0, max(_FoamDistance, 1e-4), waterDepth));
                foam *= _FoamStrength;
                float3 color = lerp(refraction, reflection, fresnel);
                color += caustics * transmittance * (_CausticStrength * (1.0 - fresnel));
                color += volumeColor * (1.0 - fresnel);
                color += scatter * (1.0 - fresnel);
                color += subsurface;
                color += specularColor;
                float crest = saturate(length(normalWS - normalize(input.normalWS)) * 4.0);
                color = lerp(color, _FoamColor.rgb, crest * 0.18);
                color = lerp(color, _FoamColor.rgb, saturate(foam));

                float alpha = saturate(_Opacity + fresnel * 0.2 + depthFactor * 0.08 + foam);
                return float4(color, alpha);
            }
            ENDHLSL
        }
    }
}
