Shader "MaterialFX/JadeVolume/PreviewBackground"
{
    Properties
    {
        _BackgroundColor("Background Color", Color) = (0.84, 0.88, 0.74, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry-10" }
        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                float4 _BackgroundColor;
            CBUFFER_END

            float3 SanitizeColor(float3 c)
            {
                c = any(isnan(c)) ? 0.0 : c;
                c = any(isinf(c)) ? 0.0 : c;
                return max(c, 0.0);
            }

            float3 RenderBackground(float2 uv)
            {
                return SanitizeColor(_BackgroundColor.rgb);
            }

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                return float4(RenderBackground(i.uv), 1.0);
            }
            ENDHLSL
        }
    }
}
