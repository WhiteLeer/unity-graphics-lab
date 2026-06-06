Shader "MaterialFX/Crystal/KaleidoscopeBackground"
{
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

            float3 SanitizeColor(float3 c)
            {
                c = any(isnan(c)) ? 0.0.xxx : c;
                c = any(isinf(c)) ? 0.0.xxx : c;
                return max(c, 0.0);
            }

            float3 RenderBackground(float2 uv)
            {
                uv -= 0.5;
                uv.x *= _ScreenParams.x / max(_ScreenParams.y, 1.0);

                float3 baseColor = float3(0.35, 0.25, 0.45);
                float3 rayDirection = normalize(float3(uv, 1.0));
                float3 gradient = length(pow(abs(rayDirection + float3(0.0, 0.5, 0.0)), 3.0)) * 0.3;
                return SanitizeColor(baseColor + gradient);
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
