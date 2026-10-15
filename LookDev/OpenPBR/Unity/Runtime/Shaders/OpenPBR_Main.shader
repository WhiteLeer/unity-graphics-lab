Shader "UnityGraphicsLab/OpenPBR/Main"
{
    Properties
    {
        [Header(Base)]
        [MainColor] _BaseColor("Base Color", Color) = (0.8, 0.8, 0.8, 1)
        _BaseWeight("Base Weight", Range(0, 1)) = 1
        _BaseDiffuseRoughness("Diffuse Roughness", Range(0, 1)) = 0
        _BaseMetalness("Metalness", Range(0, 1)) = 0

        [Header(Subsurface)]
        _SubsurfaceWeight("Subsurface Weight", Range(0, 1)) = 0
        _SubsurfaceColor("Subsurface Color", Color) = (0.8, 0.8, 0.8, 1)
        _SubsurfaceRadius("Subsurface Radius", Float) = 1
        _SubsurfaceRadiusScale("Subsurface Radius Scale", Vector) = (1, 0.5, 0.25, 0)
        _SubsurfaceScatterAnisotropy("Subsurface Scatter Anisotropy", Range(-1, 1)) = 0

        [Header(Specular)]
        _SpecularWeight("Specular Weight", Range(0, 1)) = 1
        _SpecularColor("Specular Color", Color) = (1, 1, 1, 1)
        _SpecularRoughness("Specular Roughness", Range(0, 1)) = 0.3
        _SpecularRoughnessAnisotropy("Specular Roughness Anisotropy", Range(-1, 1)) = 0
        _SpecularIOR("Specular IOR", Range(1.001, 3)) = 1.5
        _SpecularAnisotropyRotation("Specular Anisotropy Rotation", Range(-180, 180)) = 0

        [Header(Coat)]
        _CoatWeight("Coat Weight", Range(0, 1)) = 0
        _CoatColor("Coat Color", Color) = (1, 1, 1, 1)
        _CoatRoughness("Coat Roughness", Range(0, 1)) = 0
        _CoatRoughnessAnisotropy("Coat Roughness Anisotropy", Range(-1, 1)) = 0
        _CoatIOR("Coat IOR", Range(1.001, 3)) = 1.6
        _CoatDarkening("Coat Darkening", Range(0, 1)) = 1
        _CoatAnisotropyRotation("Coat Anisotropy Rotation", Range(-180, 180)) = 0

        [Header(Fuzz)]
        _FuzzWeight("Fuzz Weight", Range(0, 1)) = 0
        _FuzzColor("Fuzz Color", Color) = (1, 1, 1, 1)
        _FuzzRoughness("Fuzz Roughness", Range(0, 1)) = 0.5

        [Header(Transmission)]
        _TransmissionWeight("Transmission Weight", Range(0, 1)) = 0
        _TransmissionColor("Transmission Color", Color) = (1, 1, 1, 1)
        _TransmissionDepth("Transmission Depth", Float) = 0
        _TransmissionScatter("Transmission Scatter", Color) = (0, 0, 0, 1)
        _TransmissionScatterAnisotropy("Transmission Scatter Anisotropy", Range(-1, 1)) = 0
        _TransmissionDispersionScale("Transmission Dispersion Scale", Range(0, 1)) = 0
        _TransmissionDispersionAbbeNumber("Transmission Dispersion Abbe Number", Range(1, 200)) = 20

        [Header(Thin Film)]
        _ThinFilmWeight("Thin Film Weight", Range(0, 1)) = 0
        _ThinFilmThickness("Thin Film Thickness", Float) = 0.5
        _ThinFilmIOR("Thin Film IOR", Range(1.001, 3)) = 1.4

        [Header(Emission)]
        _EmissionColor("Emission Color", Color) = (1, 1, 1, 1)
        _EmissionLuminance("Emission Luminance", Range(0, 10000)) = 0

        [Header(Geometry)]
        _GeometryOpacity("Geometry Opacity", Range(0, 1)) = 1
        [Toggle] _GeometryThinWalled("Thin Walled", Float) = 0
        _ExteriorIOR("Exterior IOR", Range(1, 3)) = 1
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
            Name "OpenPBRForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GlobalIllumination.hlsl"
            #include "../URP/OpenPBR_URP_Adapter.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _BaseWeight;
                float _BaseDiffuseRoughness;
                float _BaseMetalness;
                float _SubsurfaceWeight;
                float4 _SubsurfaceColor;
                float _SubsurfaceRadius;
                float4 _SubsurfaceRadiusScale;
                float _SubsurfaceScatterAnisotropy;
                float _SpecularWeight;
                float4 _SpecularColor;
                float _SpecularRoughness;
                float _SpecularRoughnessAnisotropy;
                float _SpecularIOR;
                float _SpecularAnisotropyRotation;
                float _CoatWeight;
                float4 _CoatColor;
                float _CoatRoughness;
                float _CoatRoughnessAnisotropy;
                float _CoatIOR;
                float _CoatDarkening;
                float _CoatAnisotropyRotation;
                float _FuzzWeight;
                float4 _FuzzColor;
                float _FuzzRoughness;
                float _TransmissionWeight;
                float4 _TransmissionColor;
                float _TransmissionDepth;
                float4 _TransmissionScatter;
                float _TransmissionScatterAnisotropy;
                float _TransmissionDispersionScale;
                float _TransmissionDispersionAbbeNumber;
                float _ThinFilmWeight;
                float _ThinFilmThickness;
                float _ThinFilmIOR;
                float4 _EmissionColor;
                float _EmissionLuminance;
                float _GeometryOpacity;
                float _GeometryThinWalled;
                float _ExteriorIOR;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 tangentWS : TEXCOORD2;
                float3 viewDirectionWS : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                float tangentHandedness : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            UnityOpenPBRMaterial BuildOpenPBRMaterial(
                float3 normalWS,
                float3 tangentWS,
                float tangentHandedness)
            {
                UnityOpenPBRMaterial inputs;

                inputs.baseWeight = _BaseWeight;
                inputs.baseColor = _BaseColor.rgb;
                inputs.baseDiffuseRoughness = _BaseDiffuseRoughness;
                inputs.baseMetalness = _BaseMetalness;

                inputs.subsurfaceWeight = _SubsurfaceWeight;
                inputs.subsurfaceColor = _SubsurfaceColor.rgb;
                inputs.subsurfaceRadius = max(_SubsurfaceRadius, 0.0001f);
                inputs.subsurfaceRadiusScale = max(_SubsurfaceRadiusScale.xyz, 0.0001f);
                inputs.subsurfaceScatterAnisotropy = _SubsurfaceScatterAnisotropy;

                inputs.specularWeight = _SpecularWeight;
                inputs.specularColor = _SpecularColor.rgb;
                inputs.specularRoughness = _SpecularRoughness;
                inputs.specularRoughnessAnisotropy = _SpecularRoughnessAnisotropy;
                inputs.specularIor = max(_SpecularIOR, 1.001f);
                inputs.specularAnisotropyRotation = _SpecularAnisotropyRotation;

                inputs.coatWeight = _CoatWeight;
                inputs.coatColor = _CoatColor.rgb;
                inputs.coatRoughness = _CoatRoughness;
                inputs.coatRoughnessAnisotropy = _CoatRoughnessAnisotropy;
                inputs.coatIor = max(_CoatIOR, 1.001f);
                inputs.coatDarkening = _CoatDarkening;
                inputs.coatAnisotropyRotation = _CoatAnisotropyRotation;

                inputs.fuzzWeight = _FuzzWeight;
                inputs.fuzzColor = _FuzzColor.rgb;
                inputs.fuzzRoughness = _FuzzRoughness;

                inputs.transmissionWeight = _TransmissionWeight;
                inputs.transmissionColor = _TransmissionColor.rgb;
                inputs.transmissionDepth = max(_TransmissionDepth, 0.0f);
                inputs.transmissionScatter = max(_TransmissionScatter.rgb, 0.0f);
                inputs.transmissionScatterAnisotropy = _TransmissionScatterAnisotropy;
                inputs.transmissionDispersionScale = _TransmissionDispersionScale;
                inputs.transmissionDispersionAbbeNumber = _TransmissionDispersionAbbeNumber;

                inputs.thinFilmWeight = _ThinFilmWeight;
                inputs.thinFilmThickness = max(_ThinFilmThickness, 0.0f);
                inputs.thinFilmIor = max(_ThinFilmIOR, 1.001f);

                inputs.emissionLuminance = _EmissionLuminance;
                inputs.emissionColor = _EmissionColor.rgb;
                inputs.geometryOpacity = _GeometryOpacity;
                inputs.geometryThinWalled = _GeometryThinWalled > 0.5f;
                inputs.exteriorIor = max(_ExteriorIOR, 1.0f);
                return inputs;
            }

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = SafeNormalize(normalInputs.normalWS);
                output.tangentWS = SafeNormalize(normalInputs.tangentWS);
                output.tangentHandedness = input.tangentOS.w;
                output.viewDirectionWS = GetWorldSpaceViewDir(positionInputs.positionWS);
                output.shadowCoord = TransformWorldToShadowCoord(positionInputs.positionWS);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                return output;
            }

            float3 EvaluateDirectLighting(
                UnityOpenPBRPreparedMaterial prepared,
                Varyings input)
            {
                float3 lighting = 0.0f;
                Light mainLight = GetMainLight(input.shadowCoord);
                UnityOpenPBRLightResponse mainResponse = UnityOpenPBR_EvaluatePreparedUrpLight(
                    prepared, SafeNormalize(mainLight.direction));
                lighting += (mainResponse.diffuse + mainResponse.specular + mainResponse.transmission) *
                            mainLight.color * mainLight.distanceAttenuation * mainLight.shadowAttenuation;

                #if defined(_ADDITIONAL_LIGHTS)
                    uint additionalLightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(additionalLightCount)
                        Light light = GetAdditionalLight(lightIndex, input.positionWS);
                        UnityOpenPBRLightResponse response = UnityOpenPBR_EvaluatePreparedUrpLight(
                            prepared, SafeNormalize(light.direction));
                        lighting += (response.diffuse + response.specular + response.transmission) *
                                    light.color * light.distanceAttenuation * light.shadowAttenuation;
                    LIGHT_LOOP_END
                #endif

                return lighting;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                const float3 normalWS = SafeNormalize(input.normalWS);
                const float3 viewDirectionWS = SafeNormalize(input.viewDirectionWS);
                const UnityOpenPBRMaterial material = BuildOpenPBRMaterial(
                    normalWS, input.tangentWS, 1.0f);
                const UnityOpenPBRPreparedMaterial prepared = UnityOpenPBR_PrepareUrpMaterial(
                    material, normalWS, input.tangentWS, input.tangentHandedness, viewDirectionWS);

                float3 color = EvaluateDirectLighting(prepared, input);
                UnityOpenPBRLightResponse ambientResponse = UnityOpenPBR_EvaluatePreparedUrpLight(
                    prepared, normalWS);
                color += ambientResponse.diffuse * SampleSH(normalWS);

                const float3 reflectionDirection = reflect(-viewDirectionWS, normalWS);
                const float3 environment = GlossyEnvironmentReflection(
                    reflectionDirection, input.positionWS, saturate(_SpecularRoughness), 1.0h);
                UnityOpenPBRLightResponse environmentResponse = UnityOpenPBR_EvaluatePreparedUrpLight(
                    prepared, SafeNormalize(reflectionDirection));
                color += environment * environmentResponse.specular;

                if (_TransmissionWeight > 0.0f && material.geometryThinWalled <= 0.5f)
                {
                    const float3 transmissionIor = float3(
                        UnityOpenPBR_GetDispersionIor(material, 650.0f),
                        UnityOpenPBR_GetDispersionIor(material, 550.0f),
                        UnityOpenPBR_GetDispersionIor(material, 450.0f));
                    const float3 transmissionDirection = SafeNormalize(
                        refract(-viewDirectionWS, normalWS, rcp(transmissionIor.g)));
                    const float3 transmissionDirectionR = SafeNormalize(
                        refract(-viewDirectionWS, normalWS, rcp(transmissionIor.r)));
                    const float3 transmissionDirectionB = SafeNormalize(
                        refract(-viewDirectionWS, normalWS, rcp(transmissionIor.b)));
                    const float3 transmittedEnvironmentR = GlossyEnvironmentReflection(
                        transmissionDirectionR, input.positionWS, saturate(_SpecularRoughness), 1.0h);
                    const float3 transmittedEnvironmentG = GlossyEnvironmentReflection(
                        transmissionDirection, input.positionWS, saturate(_SpecularRoughness), 1.0h);
                    const float3 transmittedEnvironmentB = GlossyEnvironmentReflection(
                        transmissionDirectionB, input.positionWS, saturate(_SpecularRoughness), 1.0h);
                    const float3 transmittedEnvironment = float3(
                        transmittedEnvironmentR.r,
                        transmittedEnvironmentG.g,
                        transmittedEnvironmentB.b);
                    UnityOpenPBRLightResponse transmissionResponse =
                        UnityOpenPBR_EvaluatePreparedUrpLight(prepared, transmissionDirection);
                    const float3 volumeAttenuation = UnityOpenPBR_TransmissionAttenuation(
                        prepared, max(_TransmissionDepth, 0.0f));
                    color += transmittedEnvironment * transmissionResponse.transmission * volumeAttenuation;
                }

                color += UnityOpenPBR_GetEmission(prepared);
                return float4(max(color, 0.0f), saturate(_GeometryOpacity));
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
