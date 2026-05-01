Shader "Hidden/Func7/MotionBlur"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    TEXTURE2D(_BaseMap);
    SAMPLER(sampler_BaseMap);
    TEXTURE2D(_MotionVectorTexture);
    SAMPLER(sampler_MotionVectorTexture);

    float4 _MBParams;   // x: intensity, y: shutterScale, z: sampleCount, w: maxBlurPixels
    float4 _MBParams2;  // x: motionThreshold, y: centerWeight
    float4 _MBScreenSize;

    #define MB_INTENSITY _MBParams.x
    #define MB_SHUTTER_SCALE _MBParams.y
    #define MB_SAMPLES int(_MBParams.z)
    #define MB_MAX_BLUR_PIXELS _MBParams.w
    #define MB_MOTION_THRESHOLD _MBParams2.x
    #define MB_CENTER_WEIGHT _MBParams2.y

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
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "MotionBlur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float3 center = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;

                // URP motion vector texture stores velocity in UV space.
                float2 motion = SAMPLE_TEXTURE2D(_MotionVectorTexture, sampler_MotionVectorTexture, uv).xy;
                motion *= MB_INTENSITY * MB_SHUTTER_SCALE;

                float motionPixels = length(motion * _MBScreenSize.xy);
                if (motionPixels < MB_MOTION_THRESHOLD * max(_MBScreenSize.x, _MBScreenSize.y))
                    return half4(center, 1.0);

                float maxUvLen = MB_MAX_BLUR_PIXELS * max(_MBScreenSize.z, _MBScreenSize.w);
                float motionLen = length(motion);
                if (motionLen > maxUvLen)
                    motion *= maxUvLen / max(motionLen, 1e-6);

                int sampleCount = clamp(MB_SAMPLES, 4, 24);
                float3 accum = center * MB_CENTER_WEIGHT;
                float weightSum = MB_CENTER_WEIGHT;

                // Symmetric taps along motion direction: reduce directional bias.
                [loop]
                for (int i = 0; i < sampleCount; i++)
                {
                    float t = (i + 0.5) / sampleCount;
                    float2 offset = motion * t;
                    float2 uvA = saturate(uv + offset);
                    float2 uvB = saturate(uv - offset);

                    float w = 1.0 - t;
                    float3 cA = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvA).rgb;
                    float3 cB = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvB).rgb;

                    accum += (cA + cB) * w;
                    weightSum += 2.0 * w;
                }

                float3 color = accum / max(weightSum, 1e-5);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
