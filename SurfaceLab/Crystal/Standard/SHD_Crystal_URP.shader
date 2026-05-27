Shader "MaterialFX/Crystal/URP"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _DetailMap ("Detail Map", 2D) = "gray" {}
        _MaskMap ("Mask Map", 2D) = "white" {}
        _Tint ("Tint", Color) = (0.78, 0.52, 0.92, 1)
        _AbsorptionColor ("Absorption Color", Color) = (0.30, 0.08, 0.44, 1)
        _AbsorptionStrength ("Absorption Strength", Range(0, 3)) = 1.0
        _RimColor ("Rim Color", Color) = (1.0, 0.85, 1.0, 1)
        _RimPower ("Rim Power", Range(0.5, 12)) = 4
        _SpecColor ("Specular Color", Color) = (1,1,1,1)
        _SpecPower ("Specular Power", Range(8,256)) = 96
        _Smoothness ("Smoothness", Range(0,1)) = 0.9
        _RefractionStrength ("Refraction Strength", Range(0, 0.08)) = 0.006
        _DetailTiling ("Detail Tiling", Range(0.5, 16)) = 8
        _InnerGlow ("Inner Glow", Range(0, 2)) = 0.9
        _Opacity ("Opacity", Range(0.05, 1)) = 0.82
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_DetailMap);
            SAMPLER(sampler_DetailMap);
            TEXTURE2D(_MaskMap);
            SAMPLER(sampler_MaskMap);
            TEXTURE2D_X(_CameraOpaqueTexture);
            SAMPLER(sampler_CameraOpaqueTexture);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _DetailMap_ST;
                float4 _MaskMap_ST;
                float4 _Tint;
                float4 _AbsorptionColor;
                float _AbsorptionStrength;
                float4 _RimColor;
                float _RimPower;
                float4 _SpecColor;
                float _SpecPower;
                float _Smoothness;
                float _RefractionStrength;
                float _DetailTiling;
                float _InnerGlow;
                float _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uvBase : TEXCOORD2;
                float2 uvDetail : TEXCOORD3;
                float2 uvMask : TEXCOORD4;
                float3 viewDirWS : TEXCOORD5;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs posInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInput = GetVertexNormalInputs(input.normalOS);
                o.positionHCS = posInput.positionCS;
                o.positionWS = posInput.positionWS;
                o.normalWS = normalize(normInput.normalWS);
                o.uvBase = TRANSFORM_TEX(input.uv, _BaseMap);
                o.uvDetail = TRANSFORM_TEX(input.uv, _DetailMap) * _DetailTiling;
                o.uvMask = TRANSFORM_TEX(input.uv, _MaskMap);
                o.viewDirWS = GetWorldSpaceViewDir(posInput.positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewDirWS);
                float ndv = saturate(dot(n, v));

                float4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uvBase);
                float4 detailTex = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, i.uvDetail);
                float4 maskTex = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, i.uvMask);

                float2 distortionVec = ((detailTex.rg * 2.0 - 1.0) * 0.6 + n.xz * 0.4);
                float2 screenUV = i.positionHCS.xy / _ScaledScreenParams.xy;
                float2 refractUV = screenUV + distortionVec * _RefractionStrength;
                float3 sceneColor = SAMPLE_TEXTURE2D_X(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, refractUV).rgb;

                float4 albedo = baseTex * _Tint;
                Light mainLight = GetMainLight();
                float3 l = normalize(mainLight.direction);
                float3 h = normalize(l + v);

                float lambert = saturate(dot(n, l));
                float ndh = saturate(dot(n, h));
                float specPrimary = pow(ndh, _SpecPower) * _Smoothness;
                float specSecondary = pow(ndh, max(8.0, _SpecPower * 0.35)) * (1.0 - ndv) * 0.7;

                float fresnel = pow(1.0 - ndv, _RimPower);
                float3 rim = _RimColor.rgb * fresnel;

                float thickness = saturate((1.0 - ndv) * 0.7 + maskTex.g * 0.7);
                float3 absorption = _AbsorptionColor.rgb * thickness * _AbsorptionStrength;
                float3 transmission = sceneColor * albedo.rgb;
                float3 body = lerp(albedo.rgb * 0.85, transmission, 0.35);
                float bands = abs(frac(i.positionWS.y * 2.2 + detailTex.r * 0.2) - 0.5) * 2.0;
                float cracks = smoothstep(0.72, 0.96, detailTex.g);
                float bandMask = 1.0 - smoothstep(0.2, 0.8, bands);
                float3 crystalStrata = lerp(float3(1.0, 1.0, 1.0), _AbsorptionColor.rgb, bandMask * 0.25);
                body *= crystalStrata;
                body -= cracks * _AbsorptionColor.rgb * 0.10;

                float sparkle = smoothstep(0.74, 0.96, detailTex.b) * maskTex.r;
                float3 sparkleColor = _SpecColor.rgb * sparkle * 0.35;

                float3 lit = body * (0.2 + lambert * mainLight.color.rgb);
                float3 specular = _SpecColor.rgb * (specPrimary + specSecondary) * 1.35;
                float3 innerGlow = _RimColor.rgb * thickness * _InnerGlow * 0.25;
                float3 finalColor = lit + specular + rim + innerGlow + sparkleColor - absorption;

                float alpha = saturate(max(0.78, _Opacity + fresnel * 0.12 + maskTex.r * 0.08));
                return float4(saturate(finalColor), alpha);
            }
            ENDHLSL
        }
    }
}
