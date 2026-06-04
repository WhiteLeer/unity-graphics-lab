Shader "Hidden/SSPR"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

    TEXTURE2D(_BaseMap);
    SAMPLER(sampler_BaseMap);
    Texture2D<uint> _SSPR_Offset;
    Texture2D<uint> _SSPR_Depth;

    // SSPR参数
    float4 _SSPRParams; // x: Intensity, y: FresnelPower, z: FadeStart, w: FadeEnd
    float4 _SSPRParams2; // x: NormalThreshold, y: MaxPlaneDistance, z: HoleFillRadius, w: HoleFillEnabled
    float4 _SSPRParams3; // x: Roughness, y: MaxBlurPixels
    float4 _SSPRReflectorCenter;
    float4 _SSPRReflectorHalfSize;
    float4 _SSPRReflectorU;
    float4 _SSPRReflectorV;
    float _SSPRUseReflectorBounds;
    float4 _ReflectionPlane; // xyz: Normal, w: Distance (平面方程: dot(pos, normal) = distance)
    float4x4 _SSPRInvViewProj;
    float4x4 _SSPRViewProj;
    float4x4 _SSPRView;
    float _SSPROcclusionThickness;
    float _SSPRDepthConsistency;
    float4 _SSPRDebugFlags;

    // View空间重建参数（复用SSAO的方法）
    float4 _CameraViewTopLeftCorner;
    float4 _CameraViewXExtent;
    float4 _CameraViewYExtent;
    float4 _ProjectionParams2; // x: 1/nearClipPlane

    // Debug模式（和SSAO一致）
    int _DebugMode; // -1=None, 1=Depth, 5=ViewPosVector, 16=WorldPosition
    float4 _SSPRScreenSize; // x: width, y: height, z: 1/width, w: 1/height

    #define INTENSITY _SSPRParams.x
    #define FRESNEL_POWER _SSPRParams.y
    #define FADE_START _SSPRParams.z
    #define FADE_END _SSPRParams.w
    #define NORMAL_THRESHOLD _SSPRParams2.x
    #define MAX_PLANE_DISTANCE _SSPRParams2.y
    #define HOLE_FILL_RADIUS _SSPRParams2.z
    #define HOLE_FILL_ENABLED _SSPRParams2.w
    #define ROUGHNESS _SSPRParams3.x
    #define MAX_BLUR_PIXELS _SSPRParams3.y
    #define INVALID_OFFSET 0xFFFFFFFFu

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

    // Unity官方方法：重建从相机到像素的世界空间向量（和SSAO完全一致）
    float3 ReconstructViewPos(float2 uv, float depth)
    {
        uv.y = 1.0 - uv.y;
        float zScale = depth * _ProjectionParams2.x;
        float3 viewPos = _CameraViewTopLeftCorner.xyz
            + _CameraViewXExtent.xyz * uv.x
            + _CameraViewYExtent.xyz * uv.y;
        viewPos *= zScale;
        return viewPos;
    }

    // 计算关于平面的镜像位置
    float3 MirrorPositionAcrossPlane(float3 worldPos, float3 planeNormal, float planeDistance)
    {
        // 点到平面的距离
        float dist = dot(worldPos, planeNormal) - planeDistance;

        // 镜像位置 = 原位置 - 2 * 距离 * 法线
        return worldPos - 2.0 * dist * planeNormal;
    }

    // Schlick菲涅尔近似
    float FresnelSchlick(float3 viewDir, float3 normal, float power)
    {
        float cosTheta = saturate(dot(viewDir, normal));
        return pow(1.0 - cosTheta, power);
    }

    // 边界淡出
    float EdgeFade(float2 uv, float fadeStart, float fadeEnd)
    {
        // 如果fadeStart和fadeEnd相同，禁用淡出（返回1）
        if (abs(fadeEnd - fadeStart) < 0.001)
            return 1.0;

        float2 dist = min(uv, 1.0 - uv);
        float minDist = min(dist.x, dist.y);
        return smoothstep(fadeStart, fadeEnd, minDist);
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }
        Cull Off ZWrite Off ZTest Always

        // Pass 0: SSPR计算
        Pass
        {
            Name "SSPR"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment SSPR_Frag

            half4 SSPR_Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                half4 sceneColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);

                // 1. 采样深度
                float rawDepth = SampleSceneDepth(uv);
                if (rawDepth < 0.00001)
                    return sceneColor;

                // 转换为线性深度（和SSAO一致）
                float depth = LinearEyeDepth(rawDepth, _ZBufferParams);

                // Debug模式（和SSAO一致）
                if (_DebugMode == 1)
                {
                    // 显示深度
                    float normalized = depth / 100.0;
                    return half4(normalized.xxx, 1.0);
                }

                // 重建从相机到该点的世界空间向量（和SSAO一致）
                float3 viewPosVec = ReconstructViewPos(uv, depth);

                if (_DebugMode == 5)
                {
                    // 显示从相机到该点的世界空间向量
                    float3 visPos = frac(viewPosVec * 0.1);
                    return half4(visPos, 1);
                }

                // 使用invViewProj重建世界坐标（与Compute一致）
                float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, _SSPRInvViewProj);

                if (_DebugMode == 16)
                {
                    // 显示完整的世界坐标
                    float3 normalized = float3(
                        worldPos.x * 0.05 + 0.5,
                        worldPos.y * 0.1,
                        worldPos.z * 0.05 + 0.5
                    );
                    return half4(saturate(normalized), 1);
                }

                // 3. 采样法线，判断是否为反射表面
                float3 worldNormal = SampleSceneNormals(uv);

                if (_DebugMode == 10)
                {
                    // 显示法线
                    return half4(worldNormal * 0.5 + 0.5, 1);
                }

                float normalLen = dot(worldNormal, worldNormal);
                if (normalLen < 0.25)
                {
                    // 法线贴图不可用时，退化为平面法线，避免整块被过滤掉
                    worldNormal = normalize(_ReflectionPlane.xyz);
                }
                float normalDot = dot(normalize(worldNormal), normalize(_ReflectionPlane.xyz));

                if (_DebugMode == 11)
                {
                    // 显示法线点乘结果（接近1=朝上，接近0=垂直，<0=朝下）
                    return half4(normalDot, normalDot, normalDot, 1);
                }

                if (_SSPRDebugFlags.x < 0.5 && normalDot < NORMAL_THRESHOLD)
                    return sceneColor;

                // 4. 距离检查
                float distToPlane = abs(dot(worldPos, _ReflectionPlane.xyz) - _ReflectionPlane.w);

                if (_DebugMode == 2)
                {
                    // 显示到平面的距离（归一化到0-5范围）
                    float vis = saturate(distToPlane / 5.0);
                    return half4(vis, vis, vis, 1);
                }
                float receiverPlaneTolerance = MAX_PLANE_DISTANCE >= 0.02 ? max(MAX_PLANE_DISTANCE, 0.08) : 0.08;
                bool nearPlane = distToPlane <= receiverPlaneTolerance;

                // Use plane proximity as the receiver test for now.
                // The normal-based filter is too unstable in the current URP setup and can remove the entire reflector.
                if (!nearPlane)
                    return sceneColor;

                if (_SSPRUseReflectorBounds > 0.5)
                {
                    float distSigned = dot(worldPos, _ReflectionPlane.xyz) - _ReflectionPlane.w;
                    float3 planePoint = worldPos - distSigned * _ReflectionPlane.xyz;
                    float3 rel = planePoint - _SSPRReflectorCenter.xyz;
                    float u = dot(rel, _SSPRReflectorU.xyz);
                    float v = dot(rel, _SSPRReflectorV.xyz);
                    if (abs(u) > _SSPRReflectorHalfSize.x || abs(v) > _SSPRReflectorHalfSize.y)
                        return sceneColor;
                }

                // 5. PPT/PPR path: read the precomputed receiver->source mapping.
                uint2 pixel = uint2(input.uv * _SSPRScreenSize.xy);
                uint packed = _SSPR_Offset.Load(int3(pixel, 0));
                uint packedDepth = _SSPR_Depth.Load(int3(pixel, 0));

                if (packed == INVALID_OFFSET && HOLE_FILL_ENABLED > 0.5)
                {
                    int radius = (int)HOLE_FILL_RADIUS;
                    uint bestPacked = INVALID_OFFSET;
                    uint bestPackedDepth = INVALID_OFFSET;
                    float bestDist = 1e9;
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int2 p = int2(pixel) + int2(dx, dy);
                            if (p.x < 0 || p.y < 0 || p.x >= (int)_SSPRScreenSize.x || p.y >= (int)_SSPRScreenSize.y)
                                continue;
                            uint candidate = _SSPR_Offset.Load(int3(p, 0));
                            if (candidate == INVALID_OFFSET)
                                continue;
                            float d = (float)(dx * dx + dy * dy);
                            if (d < bestDist)
                            {
                                bestDist = d;
                                bestPacked = candidate;
                                bestPackedDepth = _SSPR_Depth.Load(int3(p, 0));
                            }
                        }
                    }
                    packed = bestPacked;
                    packedDepth = bestPackedDepth;
                }

                if (packed == INVALID_OFFSET)
                    return sceneColor;

                if (packedDepth == INVALID_OFFSET)
                    return sceneColor;

                float hitDepth = asfloat(packedDepth);
                if (!isfinite(hitDepth) || hitDepth <= 0.0001)
                    return sceneColor;

                if (_SSPRDebugFlags.y < 0.5 && _SSPRDepthConsistency > 0.005)
                {
                    float receiverDepth = -mul(_SSPRView, float4(worldPos, 1.0)).z;
                    // Keep this tolerance intentionally wider than SSR-style hit tests.
                    // SSPR projection map has raster quantization and can differ by a few centimeters.
                    float depthTolerance = max(_SSPRDepthConsistency, receiverDepth * 0.01);
                    depthTolerance = max(depthTolerance, _SSPROcclusionThickness);
                    if (abs(receiverDepth - hitDepth) > depthTolerance)
                        return sceneColor;
                }

                uint srcX = packed & 0xFFFFu;
                uint srcY = packed >> 16;
                float2 hitUV = (float2(srcX, srcY) + 0.5) * _SSPRScreenSize.zw;

                if (_DebugMode == 19)
                {
                    return half4(0, 1, 0, 1);
                }

                if (_DebugMode == 22)
                {
                    return half4(hitUV, 0, 1);
                }

                half4 reflectionColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, hitUV);
                if (ROUGHNESS > 0.001 && MAX_BLUR_PIXELS > 0.0)
                {
                    float radius = ROUGHNESS * MAX_BLUR_PIXELS;
                    float2 stepUV = radius * _SSPRScreenSize.zw;
                    half4 c0 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, hitUV + float2(stepUV.x, 0));
                    half4 c1 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, hitUV - float2(stepUV.x, 0));
                    half4 c2 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, hitUV + float2(0, stepUV.y));
                    half4 c3 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, hitUV - float2(0, stepUV.y));
                    reflectionColor = (reflectionColor + c0 + c1 + c2 + c3) * 0.2;
                }

                if (_DebugMode == 20)
                {
                    // 显示采样的反射颜色（不混合）
                    return reflectionColor;
                }

                // 7. 计算反射强度
                float edgeFade = EdgeFade(uv, FADE_START, FADE_END);
                float3 viewDir = normalize(_WorldSpaceCameraPos - worldPos);
                float fresnel = FresnelSchlick(viewDir, worldNormal, FRESNEL_POWER);

                if (_DebugMode == 21)
                {
                    // 显示边缘淡出遮罩
                    return half4(edgeFade, edgeFade, edgeFade, 1);
                }

                // Keep a base reflectance so the reflection does not disappear at near-normal view angles.
                float reflectionStrength = INTENSITY * edgeFade * lerp(0.08, 1.0, saturate(fresnel));

                // 9. 混合
                return lerp(sceneColor, reflectionColor, reflectionStrength);
            }
            ENDHLSL
        }
    }
}
