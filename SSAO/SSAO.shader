Shader "Hidden/SSAO"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

    TEXTURE2D(_BaseMap);
    SAMPLER(sampler_BaseMap);
    TEXTURE2D(_SSAO_Texture);
    SAMPLER(sampler_SSAO_Texture);

    // SSAO参数（与Unity一致）
    float4 _SSAOParams; // x: Intensity, y: Radius, z: Beta, w: SampleCount
    float4 _SourceSize;
    float4 _ProjectionParams2; // x: 1/nearClipPlane
    float4 _CameraViewTopLeftCorner;
    float4 _CameraViewXExtent;
    float4 _CameraViewYExtent;
    float4 _CameraViewZRow; // View矩阵第三行（用于将世界空间向量转换为View空间Z深度）
    float4x4 _CameraViewProjections; // proj * cview矩阵
    int _DebugMode; // Debug模式：-1=None, 5=ViewPos, 6=RawAO, 7=AOAfterNormalize

    #define INTENSITY _SSAOParams.x
    #define RADIUS _SSAOParams.y
    #define BETA _SSAOParams.z
    #define SAMPLE_COUNT int(_SSAOParams.w)

    // Unity常量
    static const half kContrast = 0.6;
    static const half kEpsilon = 0.0001;
    static const half kGeometryCoeff = 0.8;

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

    // Unity的随机数表
    static half SSAORandomUV[40] = {
        0.00000000, 0.33984375, 0.75390625, 0.56640625, 0.98437500,
        0.07421875, 0.23828125, 0.64062500, 0.35937500, 0.50781250,
        0.38281250, 0.98437500, 0.17578125, 0.53906250, 0.28515625,
        0.23137260, 0.45882360, 0.54117650, 0.12941180, 0.64313730,
        0.92968750, 0.76171875, 0.13333330, 0.01562500, 0.00000000,
        0.10546875, 0.64062500, 0.74609375, 0.67968750, 0.35156250,
        0.49218750, 0.12500000, 0.26562500, 0.62500000, 0.44531250,
        0.17647060, 0.44705890, 0.93333340, 0.87058830, 0.56862750
    };

    half GetRandomUVForSSAO(float u, int sampleIndex)
    {
        return SSAORandomUV[int(u * 20) + sampleIndex];
    }

    // CosSin辅助函数
    half2 CosSin(half theta)
    {
        half sn, cs;
        sincos(theta, sn, cs);
        return half2(cs, sn);
    }

    // Unity官方方法：转换原始深度到线性眼空间深度
    float GetLinearEyeDepth(float rawDepth)
    {
        return LinearEyeDepth(rawDepth, _ZBufferParams);
    }

    // Unity的深度采样
    float SampleAndGetLinearEyeDepth(float2 uv)
    {
        float rawDepth = SampleSceneDepth(uv);
        return GetLinearEyeDepth(rawDepth);
    }

    // Unity官方方法：重建从相机到像素的世界空间向量
    // 注意：返回的不是View空间坐标，而是世界空间中从相机到该点的向量（单位：世界单位）
    // Unity官方注释："This returns a vector in world unit (not a position), from camera to the given point"
    float3 ReconstructViewPos(float2 uv, float depth)
    {
        // Screen is y-inverted
        uv.y = 1.0 - uv.y;

        // 计算从相机到该点的世界空间向量
        float zScale = depth * _ProjectionParams2.x; // divide by near plane
        float3 viewPos = _CameraViewTopLeftCorner.xyz
            + _CameraViewXExtent.xyz * uv.x
            + _CameraViewYExtent.xyz * uv.y;
        viewPos *= zScale;

        return viewPos;
    }

    // 将世界空间法线转换到View Space
    half3 WorldToViewNormal(half3 normalWS)
    {
        // 使用View矩阵的旋转部分（3x3）转换法线
        // 注意：View空间中相机看向-Z，可能需要调整法线方向
        return normalize(mul((float3x3)UNITY_MATRIX_V, normalWS));
    }

    // Unity的采样点生成
    half3 PickSamplePoint(float2 uv, float randAddon, int sampleIndex)
    {
        const float2 positionSS = uv * _SourceSize.xy;
        const half gn = half(InterleavedGradientNoise(positionSS, sampleIndex));

        const half u = frac(GetRandomUVForSSAO(half(0.0), sampleIndex + randAddon) + gn) * half(2.0) - half(1.0);
        const half theta = (GetRandomUVForSSAO(half(1.0), sampleIndex + randAddon) + gn) * half(TWO_PI);

        return half3(CosSin(theta) * sqrt(half(1.0) - u * u), u);
    }

    // Pack/Unpack（用于模糊）
    half4 PackAONormal(half ao, half3 n)
    {
        return half4(ao, n * 0.5 + 0.5);
    }

    half GetPackedAO(half4 p)
    {
        return p.r;
    }

    half3 GetPackedNormal(half4 p)
    {
        return p.gba * 2.0 - 1.0;
    }

    half CompareNormal(half3 n1, half3 n2)
    {
        return smoothstep(kGeometryCoeff, 1.0, dot(n1, n2));
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }
        Cull Off ZWrite Off ZTest Always

        // Pass 0: SSAO计算（Morgan 2011算法）
        Pass
        {
            Name "SSAO"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment SSAO_Frag

            half4 SSAO_Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;

                // Unity官方方法：先获取rawDepth，用于天空判断
                float rawDepth_o = SampleSceneDepth(uv);
                if (rawDepth_o < 0.00001)  // SKY_DEPTH_VALUE
                    return PackAONormal(0.0, 0.0);

                // 转换为线性深度
                float depth_o = GetLinearEyeDepth(rawDepth_o);

                // 采样法线、重建位置
                half3 norm_o = SampleSceneNormals(uv); // 世界空间法线
                half3 vpos_o = ReconstructViewPos(uv, depth_o); // 从相机到该点的世界空间向量

                // 某些材质/通道下法线纹理可能无效（接近0向量），此时回退为无AO，避免角色整片变黑。
                if (dot(norm_o, norm_o) < 1e-4)
                {
                    return PackAONormal(0.0, half3(0.0, 0.0, 1.0));
                }

                // Debug: 显示从相机到该点的世界空间向量
                if (_DebugMode == 5)
                {
                    // vpos_o是世界空间向量，可视化其分布
                    float3 visPos = frac(vpos_o * 0.1); // 使用frac显示空间分布
                    return half4(visPos, 1);
                }

                // Debug: 显示完整的世界坐标（相机位置 + 向量）
                if (_DebugMode == 16)
                {
                    // 计算完整的世界坐标
                    float3 worldPos = _WorldSpaceCameraPos + vpos_o;

                    // 归一化到可见范围（假设场景范围：X[-10,10], Y[0,10], Z[-10,10]）
                    float3 normalized = float3(
                        worldPos.x * 0.05 + 0.5,  // X: -10~10 -> 0~1
                        worldPos.y * 0.1,         // Y: 0~10 -> 0~1
                        worldPos.z * 0.05 + 0.5   // Z: -10~10 -> 0~1
                    );

                    return half4(saturate(normalized), 1);
                }

                // Debug: 显示世界空间法线
                if (_DebugMode == 10)
                {
                    return half4(norm_o * 0.5 + 0.5, 1);
                }

                // This was added to avoid a NVIDIA driver issue.
                float randAddon = uv.x * 1e-10;

                // Morgan 2011 Alchemy AO
                float rcpSampleCount = rcp(float(SAMPLE_COUNT));
                float ao = 0.0;

                // Debug变量（仅第一个采样点）
                float2 debug_sampleUV = float2(0, 0);
                float debug_depthDiff = 0.0;
                float debug_radiusCheck = 0.0;
                float debug_zDist = 0.0;
                float debug_depth_s1 = 0.0;
                float debug_v_s2_length = 0.0;

                // 预提取矩阵元素（Unity官方方法，循环外）
                float3 camTransform000102 = float3(_CameraViewProjections._m00, _CameraViewProjections._m01, _CameraViewProjections._m02);
                float3 camTransform101112 = float3(_CameraViewProjections._m10, _CameraViewProjections._m11, _CameraViewProjections._m12);

                [loop]
                for (int s = 0; s < SAMPLE_COUNT; s++)
                {
                    // Sample point
                    float3 v_s1 = PickSamplePoint(uv, randAddon, s);

                    // Make it distributed between [0, _Radius]
                    v_s1 *= sqrt((float(s) + 1.0) * rcpSampleCount) * RADIUS;

                    v_s1 = faceforward(v_s1, -norm_o, v_s1);

                    float3 vpos_s1 = vpos_o + v_s1;

                    // Unity官方方法：只用矩阵前两行的3x3部分投影
                    float2 spos_s1 = float2(
                        dot(camTransform000102, vpos_s1),
                        dot(camTransform101112, vpos_s1)
                    );

                    // 将世界空间向量转换为View空间Z深度（Unity官方方法）
                    // vpos_s1是从相机到采样点的世界空间向量，用View矩阵Z行投影得到View空间深度
                    float zDist = -dot(_CameraViewZRow.xyz, vpos_s1);
                    float2 uv_s1_01 = saturate((spos_s1 / zDist + 1.0) * 0.5);

                    // 采样深度
                    float rawDepth_s = SampleSceneDepth(uv_s1_01);
                    float depth_s1 = GetLinearEyeDepth(rawDepth_s);

                    // 检查是否是天空盒（Unity官方：没有显式radius check，通过公式自然衰减）
                    float isInsideRadius = rawDepth_s > 0.00001 ? 1.0 : 0.0;  // SKY_DEPTH_VALUE

                    // Debug: 记录第一个采样点的信息
                    float radiusDiff = abs(zDist - depth_s1);
                    if (s == 0)
                    {
                        debug_sampleUV = uv_s1_01;
                        debug_depthDiff = depth_s1 - depth_o;
                        debug_radiusCheck = radiusDiff / RADIUS;  // 归一化到radius
                    }

                    // Relative position of the sample point
                    float3 vpos_s2 = ReconstructViewPos(uv_s1_01, depth_s1);
                    float3 v_s2 = vpos_s2 - vpos_o;

                    // Debug: 记录第一个采样点的v_s2信息
                    if (s == 0)
                    {
                        debug_zDist = zDist;
                        debug_depth_s1 = depth_s1;
                        debug_v_s2_length = length(v_s2);
                    }

                    // Morgan 2011公式
                    float a1 = max(dot(v_s2, norm_o) - BETA * depth_o, 0.0);
                    float a2 = dot(v_s2, v_s2) + kEpsilon;
                    ao += a1 * rcp(a2) * isInsideRadius;
                }

                // Debug: 显示第一个采样点的UV坐标
                if (_DebugMode == 8)
                {
                    return half4(debug_sampleUV.x, debug_sampleUV.y, 0, 1);
                }

                // Debug: 显示采样点深度差异
                if (_DebugMode == 9)
                {
                    // 归一化到可视范围
                    float visDiff = debug_depthDiff * 10.0 + 0.5; // ±0.05深度差映射到0-1
                    return half4(visDiff, visDiff, visDiff, 1);
                }

                // Debug: 显示radius检查差值（radiusDiff相对于RADIUS）
                if (_DebugMode == 11)
                {
                    // 显示radiusDiff / RADIUS的值
                    // < 1.0 = 绿色（在半径内）
                    // >= 1.0 = 红色（超出半径）
                    float vis = saturate(debug_radiusCheck);  // 归一化到[0,1]
                    return debug_radiusCheck < 1.0 ? half4(0, vis, 0, 1) : half4(vis, 0, 0, 1);
                }

                // Debug: 显示zDist vs depth_s1对比
                if (_DebugMode == 12)
                {
                    // R通道: zDist（投影深度）
                    // G通道: depth_s1（实际深度）
                    // 都归一化到0-1（假设最大深度100）
                    float visZ = saturate(debug_zDist / 100.0);
                    float visD = saturate(debug_depth_s1 / 100.0);
                    return half4(visZ, visD, 0, 1);
                }

                // Debug: 显示v_s2的长度（采样点到中心点的真实3D距离）
                if (_DebugMode == 13)
                {
                    // 归一化到RADIUS
                    float vis = saturate(debug_v_s2_length / RADIUS);
                    // < RADIUS = 绿色，>= RADIUS = 红色
                    return debug_v_s2_length < RADIUS ? half4(0, vis, 0, 1) : half4(vis, 0, 0, 1);
                }

                // Debug: 显示zDist和depth_s1的原始值（灰度）
                if (_DebugMode == 14)
                {
                    float visZ = saturate(debug_zDist / 100.0);
                    return half4(visZ, visZ, visZ, 1);
                }

                if (_DebugMode == 15)
                {
                    float visD = saturate(debug_depth_s1 / 100.0);
                    return half4(visD, visD, visD, 1);
                }

                // Debug: 显示原始AO值（归一化前）
                if (_DebugMode == 6)
                {
                    float rawAO = ao * rcpSampleCount;
                    return half4(rawAO, rawAO, rawAO, 1);
                }

                // 归一化
                ao *= RADIUS;
                float aoBeforePow = ao * INTENSITY * rcpSampleCount;

                // Debug: 显示归一化后、pow前的AO值
                if (_DebugMode == 7)
                {
                    return half4(aoBeforePow, aoBeforePow, aoBeforePow, 1);
                }

                ao = pow(abs(aoBeforePow), kContrast);
                ao = saturate(ao);

                return PackAONormal(ao, norm_o);
            }
            ENDHLSL
        }

        // Pass 1: 水平模糊
        Pass
        {
            Name "BlurH"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment BlurH_Frag

            half4 BlurH_Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float2 delta = float2(_SourceSize.z, 0.0);

                // 5-tap高斯（Unity权重）
                half4 p0 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
                half4 p1a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv - delta * 1.3846153846);
                half4 p1b = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + delta * 1.3846153846);
                half4 p2a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv - delta * 3.2307692308);
                half4 p2b = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + delta * 3.2307692308);

                half3 n0 = GetPackedNormal(p0);

                // 双边滤波权重
                half w0 = 0.2270270270;
                half w1a = CompareNormal(n0, GetPackedNormal(p1a)) * 0.3162162162;
                half w1b = CompareNormal(n0, GetPackedNormal(p1b)) * 0.3162162162;
                half w2a = CompareNormal(n0, GetPackedNormal(p2a)) * 0.0702702703;
                half w2b = CompareNormal(n0, GetPackedNormal(p2b)) * 0.0702702703;

                half ao = 0.0;
                ao += GetPackedAO(p0) * w0;
                ao += GetPackedAO(p1a) * w1a;
                ao += GetPackedAO(p1b) * w1b;
                ao += GetPackedAO(p2a) * w2a;
                ao += GetPackedAO(p2b) * w2b;
                ao /= (w0 + w1a + w1b + w2a + w2b);

                return PackAONormal(ao, n0);
            }
            ENDHLSL
        }

        // Pass 2: 垂直模糊
        Pass
        {
            Name "BlurV"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment BlurV_Frag

            half4 BlurV_Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float2 delta = float2(0.0, _SourceSize.w);

                // 5-tap高斯
                half4 p0 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
                half4 p1a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv - delta * 1.3846153846);
                half4 p1b = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + delta * 1.3846153846);
                half4 p2a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv - delta * 3.2307692308);
                half4 p2b = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + delta * 3.2307692308);

                half3 n0 = GetPackedNormal(p0);

                half w0 = 0.2270270270;
                half w1a = CompareNormal(n0, GetPackedNormal(p1a)) * 0.3162162162;
                half w1b = CompareNormal(n0, GetPackedNormal(p1b)) * 0.3162162162;
                half w2a = CompareNormal(n0, GetPackedNormal(p2a)) * 0.0702702703;
                half w2b = CompareNormal(n0, GetPackedNormal(p2b)) * 0.0702702703;

                half ao = 0.0;
                ao += GetPackedAO(p0) * w0;
                ao += GetPackedAO(p1a) * w1a;
                ao += GetPackedAO(p1b) * w1b;
                ao += GetPackedAO(p2a) * w2a;
                ao += GetPackedAO(p2b) * w2b;
                ao /= (w0 + w1a + w1b + w2a + w2b);

                return PackAONormal(ao, n0);
            }
            ENDHLSL
        }

        // Pass 3: 最终输出
        Pass
        {
            Name "Final"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Final_Frag

            half4 Final_Frag(Varyings input) : SV_Target
            {
                half4 packed = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half ao = GetPackedAO(packed);
                // Unity官方方法：反转AO值（算法输出的ao越大=遮蔽越强，需要1-ao让遮蔽处变暗）
                ao = 1.0 - ao;
                ao = saturate(ao);
                // 给一个下限保护，防止异常AO把角色压黑到“消失”。
                ao = max(ao, 0.35);
                return half4(ao, ao, ao, 1);
            }
            ENDHLSL
        }

        // Pass 4: 应用到场景
        Pass
        {
            Name "Apply"
            Blend Zero SrcColor

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Apply_Frag

            half4 Apply_Frag(Varyings input) : SV_Target
            {
                half ao = SAMPLE_TEXTURE2D(_SSAO_Texture, sampler_SSAO_Texture, input.uv).r;
                return half4(ao, ao, ao, 1);
            }
            ENDHLSL
        }
    }
}
