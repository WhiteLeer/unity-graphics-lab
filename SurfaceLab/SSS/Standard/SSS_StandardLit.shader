Shader "MaterialFX/SSS/StandardLit"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        [NoScaleOffset] _NormalMap("Normal Map", 2D) = "bump" {}
        _NormalScale("Normal Scale", Range(0, 2)) = 1

        _Smoothness("Smoothness", Range(0,1)) = 0.45
        _SpecularStrength("Specular Strength", Range(0,1)) = 0.25

        [Header(Subsurface)]
        _SubsurfaceColor("Subsurface Color", Color) = (1.0, 0.45, 0.35, 1.0)
        _SubsurfaceStrength("Subsurface Strength", Range(0,1)) = 0.5
        _Wrap("Diffuse Wrap", Range(0,1)) = 0.4

        [Header(Transmission)]
        [NoScaleOffset] _ThicknessMap("Thickness Map", 2D) = "white" {}
        _ThicknessScale("Thickness Scale", Range(0,3)) = 1.0
        _ThicknessPower("Thickness Power", Range(0.5,8)) = 2.0
        _TransmissionStrength("Transmission Strength", Range(0,2)) = 0.6
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
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_ThicknessMap); SAMPLER(sampler_ThicknessMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                half _NormalScale;
                half _Smoothness;
                half _SpecularStrength;

                half4 _SubsurfaceColor;
                half _SubsurfaceStrength;
                half _Wrap;

                half _ThicknessScale;
                half _ThicknessPower;
                half _TransmissionStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float2 staticLightmapUV : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 4);
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = nrm.normalWS;
                o.tangentWS = float4(nrm.tangentWS.xyz, input.tangentOS.w);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, o.staticLightmapUV);
                OUTPUT_SH(o.normalWS, o.vertexSH);
                return o;
            }

            half3 ApplySSSDiffuse(half3 albedo, half3 n, half3 l, half3 lightColor, half attenShadow)
            {
                half ndotl = saturate(dot(n, l));
                half wrapDiffuse = saturate((dot(n, l) + _Wrap) / (1.0h + _Wrap));
                half sssDiffuse = lerp(ndotl, wrapDiffuse, _SubsurfaceStrength);
                return albedo * lightColor * (sssDiffuse * attenShadow);
            }

            half3 ApplyTransmission(half3 n, half3 v, half3 l, half3 lightColor, half attenShadow, half thickness)
            {
                half backLit = saturate(dot(-n, l));
                half viewForward = saturate(dot(v, -l));
                half phase = pow(viewForward, _ThicknessPower);
                half trans = backLit * phase * thickness * _TransmissionStrength;
                return _SubsurfaceColor.rgb * lightColor * (trans * attenShadow);
            }

            half3 ApplySpecular(half3 n, half3 v, half3 l, half3 lightColor, half attenShadow)
            {
                half3 h = normalize(v + l);
                half ndoth = saturate(dot(n, h));
                half specPow = exp2(1.0h + _Smoothness * 10.0h);
                half spec = pow(ndoth, specPow) * _SpecularStrength;
                return lightColor * (spec * attenShadow);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                half3 albedo = baseSample.rgb;

                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv), _NormalScale);
                float sign = i.tangentWS.w * GetOddNegativeScale();
                half3 bitangent = sign * cross(i.normalWS, i.tangentWS.xyz);
                half3x3 tbn = half3x3(i.tangentWS.xyz, bitangent, i.normalWS);
                half3 n = normalize(TransformTangentToWorld(nTS, tbn));
                half3 v = SafeNormalize(GetWorldSpaceViewDir(i.positionWS));

                half thickness = SAMPLE_TEXTURE2D(_ThicknessMap, sampler_ThicknessMap, i.uv).r * _ThicknessScale;

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = v;
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.bakedGI = SAMPLE_GI(i.staticLightmapUV, i.vertexSH, n);
                inputData.shadowMask = 1;

                half3 color = albedo * inputData.bakedGI;

                Light mainLight = GetMainLight(inputData.shadowCoord);
                half mainAttenShadow = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                color += ApplySSSDiffuse(albedo, n, mainLight.direction, mainLight.color, mainAttenShadow);
                color += ApplyTransmission(n, v, mainLight.direction, mainLight.color, mainAttenShadow, thickness);
                color += ApplySpecular(n, v, mainLight.direction, mainLight.color, mainAttenShadow);

                #if defined(_ADDITIONAL_LIGHTS)
                uint lightCount = GetAdditionalLightsCount();
                for (uint li = 0u; li < lightCount; li++)
                {
                    Light l = GetAdditionalLight(li, i.positionWS);
                    half attenShadow = l.distanceAttenuation * l.shadowAttenuation;
                    color += ApplySSSDiffuse(albedo, n, l.direction, l.color, attenShadow);
                    color += ApplyTransmission(n, v, l.direction, l.color, attenShadow, thickness);
                    color += ApplySpecular(n, v, l.direction, l.color, attenShadow);
                }
                #endif

                return half4(color, baseSample.a);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/Meta"
    }
}
