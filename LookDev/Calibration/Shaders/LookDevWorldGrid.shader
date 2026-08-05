Shader "UnityGraphicsLab/LookDev/WorldGrid"
{
    Properties
    {
        [MainColor] _BaseColor("底色", Color) = (0.36, 0.41, 0.48, 1)
        _FineColor("细网格颜色", Color) = (0.25, 0.30, 0.37, 1)
        _MajorColor("粗网格颜色", Color) = (0.10, 0.14, 0.20, 1)
        _FineSize("细格尺寸", Float) = 0.1
        _MajorSize("粗格尺寸", Float) = 1.0
        _FineWidth("细线宽度", Range(0.001, 0.1)) = 0.012
        _MajorWidth("粗线宽度", Range(0.001, 0.2)) = 0.026
        _MajorBlend("粗线强度", Range(0, 1)) = 0.85
        _GridOrigin("网格原点", Vector) = (0, 0, 0, 0)
        _ProjectionSharpness("三平面投影锐度", Range(1, 32)) = 8
        _ShadowStrength("阴影接收强度", Range(0, 1)) = 0.85
        _AmbientStrength("环境光强度", Range(0, 2)) = 0.75
        _LightStrength("主光源强度", Range(0, 2)) = 0.75
        _MinimumLighting("最低亮度", Range(0, 1)) = 0.35
        _FadeStart("淡出开始距离", Float) = 8
        _FadeDistance("完全淡出距离", Float) = 40
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "WorldGrid"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _FineColor;
                half4 _MajorColor;
                float4 _GridOrigin;
                float _FineSize;
                float _MajorSize;
                float _FineWidth;
                float _MajorWidth;
                float _MajorBlend;
                float _FadeStart;
                float _FadeDistance;
                float _ProjectionSharpness;
                float _ShadowStrength;
                float _AmbientStrength;
                float _LightStrength;
                float _MinimumLighting;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            float GridLine(float2 positionWS, float cellSize, float lineWidth)
            {
                cellSize = max(cellSize, 0.0001);
                float2 scaledPosition = positionWS / cellSize;
                float2 distanceToLine = abs(frac(scaledPosition + 0.5) - 0.5);
                float2 antiAlias = max(fwidth(scaledPosition), float2(0.0001, 0.0001));
                float2 halfWidth = min(max((lineWidth / cellSize) * 0.5, 0.0001), 0.499);
                float2 lineMask = 1.0 - smoothstep(halfWidth - antiAlias, halfWidth + antiAlias, distanceToLine);
                return max(lineMask.x, lineMask.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 projectionBlend = pow(abs(normalize(input.normalWS)), max(_ProjectionSharpness, 1.0));
                projectionBlend /= max(projectionBlend.x + projectionBlend.y + projectionBlend.z, 0.0001);

                // Use YZ for X-facing surfaces, XZ for Y-facing surfaces, and XY for Z-facing surfaces.
                float fineLineX = GridLine(input.positionWS.yz - _GridOrigin.yz, _FineSize, _FineWidth);
                float fineLineY = GridLine(input.positionWS.xz - _GridOrigin.xz, _FineSize, _FineWidth);
                float fineLineZ = GridLine(input.positionWS.xy - _GridOrigin.xy, _FineSize, _FineWidth);
                float majorLineX = GridLine(input.positionWS.yz - _GridOrigin.yz, _MajorSize, _MajorWidth);
                float majorLineY = GridLine(input.positionWS.xz - _GridOrigin.xz, _MajorSize, _MajorWidth);
                float majorLineZ = GridLine(input.positionWS.xy - _GridOrigin.xy, _MajorSize, _MajorWidth);
                float fineLine = dot(float3(fineLineX, fineLineY, fineLineZ), projectionBlend);
                float majorLine = dot(float3(majorLineX, majorLineY, majorLineZ), projectionBlend);

                half3 color = lerp(_BaseColor.rgb, _FineColor.rgb, saturate(fineLine));
                color = lerp(color, _MajorColor.rgb, saturate(majorLine * _MajorBlend));

                float cameraDistance = distance(input.positionWS, _WorldSpaceCameraPos);
                float fadeRange = max(_FadeDistance - _FadeStart, 0.001);
                float fade = 1.0 - smoothstep(_FadeStart, _FadeStart + fadeRange, cameraDistance);

                color = lerp(_BaseColor.rgb, color, fade);

                float3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float normalLight = saturate(dot(normalWS, mainLight.direction));
                float shadowAttenuation = lerp(1.0, mainLight.shadowAttenuation, _ShadowStrength);
                float3 ambientLight = SampleSH(normalWS) * _AmbientStrength;
                float3 directLight = mainLight.color * normalLight * mainLight.distanceAttenuation * shadowAttenuation * _LightStrength;
                float3 lighting = max(ambientLight + directLight, _MinimumLighting.xxx);

                return half4(color * lighting, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float LerpWhiteTo(float value, float strength)
            {
                return (1.0 - strength) + value * strength;
            }
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LightMode" = "DepthOnly"
            }
            Cull Back
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            float4 DepthFrag(DepthVaryings input) : SV_Target
            {
                return 0.0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_fragment _ _WRITE_RENDERING_LAYERS

            #include "Packages/com.unity.render-pipelines.universal/Shaders/UnlitDepthNormalsPass.hlsl"
            ENDHLSL
        }
    }
}
