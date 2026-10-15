Shader "Hidden/UnityGraphicsLab/OpenPBR/CompileProbe"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../Runtime/URP/OpenPBR_URP_Adapter.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UnityOpenPBRMaterial material;
                material.baseWeight = 1.0f;
                material.baseColor = float3(0.8f, 0.8f, 0.8f);
                material.baseDiffuseRoughness = 0.0f;
                material.baseMetalness = 0.0f;
                material.subsurfaceWeight = 0.0f;
                material.subsurfaceColor = 1.0f;
                material.subsurfaceRadius = 1.0f;
                material.subsurfaceRadiusScale = float3(1.0f, 0.5f, 0.25f);
                material.subsurfaceScatterAnisotropy = 0.0f;
                material.specularWeight = 1.0f;
                material.specularColor = 1.0f;
                material.specularRoughness = 0.3f;
                material.specularRoughnessAnisotropy = 0.0f;
                material.specularIor = 1.5f;
                material.specularAnisotropyRotation = 0.0f;
                material.coatWeight = 0.0f;
                material.coatColor = 1.0f;
                material.coatRoughness = 0.0f;
                material.coatRoughnessAnisotropy = 0.0f;
                material.coatIor = 1.6f;
                material.coatDarkening = 1.0f;
                material.coatAnisotropyRotation = 0.0f;
                material.fuzzWeight = 0.0f;
                material.fuzzColor = 1.0f;
                material.fuzzRoughness = 0.5f;
                material.transmissionWeight = 0.0f;
                material.transmissionColor = 1.0f;
                material.transmissionDepth = 0.0f;
                material.transmissionScatter = 0.0f;
                material.transmissionScatterAnisotropy = 0.0f;
                material.transmissionDispersionScale = 0.0f;
                material.transmissionDispersionAbbeNumber = 20.0f;
                material.thinFilmWeight = 0.0f;
                material.thinFilmThickness = 0.5f;
                material.thinFilmIor = 1.4f;
                material.emissionColor = 1.0f;
                material.emissionLuminance = 0.0f;
                material.geometryOpacity = 1.0f;
                material.geometryThinWalled = 0.0f;
                material.exteriorIor = 1.0f;

                UnityOpenPBRPreparedMaterial prepared = UnityOpenPBR_PrepareUrpMaterial(
                    material,
                    float3(0.0f, 0.0f, 1.0f),
                    float3(1.0f, 0.0f, 0.0f),
                    1.0f,
                    float3(0.0f, 0.0f, 1.0f));
                UnityOpenPBRLightResponse response = UnityOpenPBR_EvaluatePreparedUrpLight(
                    prepared, float3(0.0f, 0.0f, 1.0f));
                return float4(response.diffuse + response.specular + response.transmission, 1.0f);
            }
            ENDHLSL
        }
    }
}
