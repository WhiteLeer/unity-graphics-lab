Shader "Hidden/Func4/Bloom"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    TEXTURE2D(_BaseMap);
    SAMPLER(sampler_BaseMap);

    TEXTURE2D(_BloomLowTex);
    SAMPLER(sampler_BloomLowTex);

    TEXTURE2D(_BloomTex);
    SAMPLER(sampler_BloomTex);

    float4 _BloomSourceTexelSize;
    float4 _BloomParams; // x: threshold, y: knee, z: clamp, w: intensity
    float4 _BloomColor;

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

    float3 Prefilter(float2 uv)
    {
        float3 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
        c = min(c, _BloomParams.z);

        float brightness = max(c.r, max(c.g, c.b));
        float threshold = _BloomParams.x;
        float knee = _BloomParams.y;

        float soft = brightness - threshold + knee;
        soft = saturate(soft / max(2.0 * knee, 1e-5));
        soft = soft * soft;

        float contrib = max(brightness - threshold, 0.0);
        contrib = max(contrib, soft * knee);
        contrib /= max(brightness, 1e-5);

        return c * contrib;
    }

    float3 Downsample(float2 uv)
    {
        float2 t = _BloomSourceTexelSize.xy;
        float3 s = 0;
        s += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + float2(-t.x, -t.y)).rgb;
        s += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + float2( t.x, -t.y)).rgb;
        s += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + float2(-t.x,  t.y)).rgb;
        s += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + float2( t.x,  t.y)).rgb;
        return s * 0.25;
    }

    float3 Upsample(float2 uv)
    {
        float3 high = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
        float3 low = SAMPLE_TEXTURE2D(_BloomLowTex, sampler_BloomLowTex, uv).rgb;
        return high + low;
    }

    half4 FragPrefilter(Varyings input) : SV_Target
    {
        return half4(Prefilter(input.uv), 1.0);
    }

    half4 FragDown(Varyings input) : SV_Target
    {
        return half4(Downsample(input.uv), 1.0);
    }

    half4 FragUp(Varyings input) : SV_Target
    {
        return half4(Upsample(input.uv), 1.0);
    }

    half4 FragComposite(Varyings input) : SV_Target
    {
        float3 src = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
        float3 bloom = SAMPLE_TEXTURE2D(_BloomTex, sampler_BloomTex, input.uv).rgb;
        bloom *= _BloomColor.rgb * _BloomParams.w;
        return half4(src + bloom, 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "BloomPrefilter"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragPrefilter
            ENDHLSL
        }

        Pass
        {
            Name "BloomDownsample"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDown
            ENDHLSL
        }

        Pass
        {
            Name "BloomUpsample"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragUp
            ENDHLSL
        }

        Pass
        {
            Name "BloomComposite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            ENDHLSL
        }
    }
}
