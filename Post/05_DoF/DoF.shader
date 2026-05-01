Shader "Hidden/Func5/DoF"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

    TEXTURE2D(_BaseMap);
    SAMPLER(sampler_BaseMap);

    TEXTURE2D(_DoFBlurTex);
    SAMPLER(sampler_DoFBlurTex);

    float4 _DoFParams1; // x=focalDistance, y=focalRange, z=maxBlurRadius, w=intensity
    float4 _DoFParams2; // x=nearStrength, y=farStrength
    float4 _DoFTexelSize; // xy=1/size

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

    float ComputeBlurAmount(float2 uv)
    {
        float rawDepth = SampleSceneDepth(uv);
        float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

        float focalDistance = max(_DoFParams1.x, 0.001);
        float focalRange = max(_DoFParams1.y, 0.001);
        float intensity = _DoFParams1.w;

        float coc = (linearDepth - focalDistance) / focalRange;
        float nearAmount = saturate(-coc * _DoFParams2.x);
        float farAmount = saturate(coc * _DoFParams2.y);
        return saturate(max(nearAmount, farAmount) * intensity);
    }

    half4 FragDownsample(Varyings input) : SV_Target
    {
        float2 uv = input.uv;
        float2 t = _DoFTexelSize.xy;
        float3 c = 0;
        c += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + float2(-t.x, -t.y)).rgb;
        c += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + float2( t.x, -t.y)).rgb;
        c += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + float2(-t.x,  t.y)).rgb;
        c += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + float2( t.x,  t.y)).rgb;
        return half4(c * 0.25, 1.0);
    }

    half4 FragBlurH(Varyings input) : SV_Target
    {
        float2 uv = input.uv;
        float r = max(0.5, _DoFParams1.z);
        float2 t = _DoFTexelSize.xy;

        float3 c = 0;
        c += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + float2(-2.0 * r * t.x, 0)).rgb * 0.1216;
        c += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + float2(-1.0 * r * t.x, 0)).rgb * 0.2333;
        c += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb * 0.2902;
        c += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + float2( 1.0 * r * t.x, 0)).rgb * 0.2333;
        c += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + float2( 2.0 * r * t.x, 0)).rgb * 0.1216;
        return half4(c, 1.0);
    }

    half4 FragBlurV(Varyings input) : SV_Target
    {
        float2 uv = input.uv;
        float r = max(0.5, _DoFParams1.z);
        float2 t = _DoFTexelSize.xy;

        float3 c = 0;
        c += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + float2(0, -2.0 * r * t.y)).rgb * 0.1216;
        c += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + float2(0, -1.0 * r * t.y)).rgb * 0.2333;
        c += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb * 0.2902;
        c += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + float2(0,  1.0 * r * t.y)).rgb * 0.2333;
        c += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + float2(0,  2.0 * r * t.y)).rgb * 0.1216;
        return half4(c, 1.0);
    }

    half4 FragComposite(Varyings input) : SV_Target
    {
        float2 uv = input.uv;
        float3 src = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
        float3 blur = SAMPLE_TEXTURE2D(_DoFBlurTex, sampler_DoFBlurTex, uv).rgb;
        float blurAmount = ComputeBlurAmount(uv);
        return half4(lerp(src, blur, blurAmount), 1.0);
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
            Name "DoFDownsample"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDownsample
            ENDHLSL
        }

        Pass
        {
            Name "DoFBlurH"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlurH
            ENDHLSL
        }

        Pass
        {
            Name "DoFBlurV"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlurV
            ENDHLSL
        }

        Pass
        {
            Name "DoFComposite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            ENDHLSL
        }
    }
}
