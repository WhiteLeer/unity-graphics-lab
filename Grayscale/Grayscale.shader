Shader "Custom/Grayscale"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white"{}
        _Intensity ("Intensity", Range(0,1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "Grayscale"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS :
                POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS :
                SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            float _Intensity;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex,
                                               sampler_MainTex, input.uv);

                // 灰度计算
                half gray = dot(color.rgb, half3(0.299, 0.587, 0.114));
                half3 grayColor = half3(gray, gray, gray);

                // 混合：根据强度在彩色和灰度之间插值    
                half3 finalColor = lerp(color.rgb, grayColor, _Intensity);

                return half4(finalColor, color.a);
            }
            ENDHLSL
        }
    }
}