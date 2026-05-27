Shader "Hidden/Func8/ColorGradingToneMapping"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    TEXTURE2D(_BaseMap);
    SAMPLER(sampler_BaseMap);

    float4 _CGParams1; // x: exposure, y: contrast, z: saturation, w: hueShift(deg)
    float4 _CGParams2; // x: postExposureEV, y: temperature, z: tint, w: toneMappingMode
    float4 _CGColorFilter;

    #define CG_EXPOSURE _CGParams1.x
    #define CG_CONTRAST _CGParams1.y
    #define CG_SATURATION _CGParams1.z
    #define CG_HUE_SHIFT _CGParams1.w

    #define CG_POST_EV _CGParams2.x
    #define CG_TEMPERATURE _CGParams2.y
    #define CG_TINT _CGParams2.z
    #define CG_TONEMAP_MODE int(_CGParams2.w + 0.5)

    struct Attributes
    {
        float4 positionOS : POSITION;
        float2 uv : TEXCOORD0;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        output.uv = input.uv;
        return output;
    }

    float3 ReinhardToneMap(float3 x)
    {
        return x / (1.0 + x);
    }

    float3 AcesToneMap(float3 x)
    {
        const float a = 2.51;
        const float b = 0.03;
        const float c = 2.43;
        const float d = 0.59;
        const float e = 0.14;
        return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
    }

    float3 ApplyWhiteBalance(float3 color, float temperature, float tint)
    {
        float t1 = temperature / 65.0;
        float t2 = tint / 65.0;

        float3 balance = float3(
            1.0 + t1 - t2 * 0.2,
            1.0,
            1.0 - t1 + t2 * 0.2
        );
        return color * max(balance, 0.001);
    }

    float3 RgbToHsv(float3 c)
    {
        float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
        float4 p = (c.g < c.b) ? float4(c.bg, K.wz) : float4(c.gb, K.xy);
        float4 q = (c.r < p.x) ? float4(p.xyw, c.r) : float4(c.r, p.yzx);
        float d = q.x - min(q.w, q.y);
        float e = 1e-10;
        return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
    }

    float3 HsvToRgb(float3 c)
    {
        float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
        float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
        return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "ColorGradingToneMapping"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                float3 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;

                // Exposure in linear space.
                float evScale = exp2(CG_POST_EV);
                col *= max(0.0, CG_EXPOSURE * evScale);

                // White balance.
                col = ApplyWhiteBalance(col, CG_TEMPERATURE, CG_TINT);

                // Color filter.
                col *= _CGColorFilter.rgb;

                // Contrast around middle gray.
                col = (col - 0.18) * CG_CONTRAST + 0.18;

                // Hue + saturation.
                float3 hsv = RgbToHsv(max(col, 0.0));
                hsv.x = frac(hsv.x + CG_HUE_SHIFT / 360.0);
                hsv.y *= CG_SATURATION;
                col = HsvToRgb(hsv);

                // Tone mapping.
                if (CG_TONEMAP_MODE == 1)
                    col = ReinhardToneMap(max(col, 0.0));
                else if (CG_TONEMAP_MODE == 2)
                    col = AcesToneMap(max(col, 0.0));

                return half4(saturate(col), 1.0);
            }
            ENDHLSL
        }
    }
}
