Shader "SurfaceLab/Water/Preview"
{
    Properties
    {
        [MainTexture] _Channel0("环境贴图", 2D) = "white" {}
        _AnimationSpeed("动画速度", Range(0, 2)) = 1
        _CameraOrbit("相机水平角", Range(-3.14159, 3.14159)) = 2.5
        _CameraHeight("相机高度", Range(-1, 1.5)) = 1.3
        _CameraRadius("相机距离", Range(0.8, 4)) = 1.6
        _OceanHeight("海平面高度", Range(-0.5, 1)) = 0.2
        _WaveBaseHeight("波浪基础高度", Range(0, 1)) = 0.5
        _WaveAmplitude("波浪幅度", Range(0, 0.8)) = 0.35
        _WaterAbsorption("水体吸收", Range(0.05, 2)) = 0.7
        [HDR] _WaterColor("水体颜色", Color) = (0.15, 0.5, 0.75, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _AnimationSpeed;
            float _CameraOrbit;
            float _CameraHeight;
            float _CameraRadius;
            float _OceanHeight;
            float _WaveBaseHeight;
            float _WaveAmplitude;
            float _WaterAbsorption;
            float4 _WaterColor;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct Varyings
        {
            float2 uv : TEXCOORD0;
            float4 positionCS : SV_POSITION;
        };

        TEXTURE2D(_Channel0);
        SAMPLER(sampler_Channel0);

        #define _STResolution (float4(_ScreenParams.xy, rcp(_ScreenParams.xy)))
        #define _STTime (_Time.y * _AnimationSpeed)

        #define PI22 3.1415927
        #define STEPS 200
        #define STEPS_GROUND 50
        #define NUM_WAVES 60
        #define OCEAN_HEIGHT _OceanHeight
        #define WAVE_BASE_HEIGHT _WaveBaseHeight
        #define WAVE_MAX_AMPLITUDE _WaveAmplitude
        #define WATER_ABSORP _WaterAbsorption
        #define WATER_COL _WaterColor.rgb
        #define SUBSURF_COL (WATER_COL * float3(1.3, 1.5, 1.1))
        #define LD normalize(float3(-1, -1, -2))
        #define MAT_OCEAN 0
        #define MAT_GROUND 1

        int gMat;
        float gCausticNoiseBlur;

        float AtanGLSL(float y, float x) { return atan2(y, x); }
        float SafeAsin(float value) { return asin(clamp(value, -1.0, 1.0)); }
        float saturate1(float v) { return clamp(v, 0.0, 1.0); }

        uint murmurHash11(uint src)
        {
            const uint M = 0x5bd1e995u;
            uint h = 1190494759u;
            src *= M;
            src ^= src >> 24u;
            src *= M;
            h *= M;
            h ^= src;
            h ^= h >> 13u;
            h *= M;
            h ^= h >> 15u;
            return h;
        }

        float hash11(float src)
        {
            uint h = murmurHash11(asuint(src));
            return asfloat((h & 0x007fffffu) | 0x3f800000u) - 1.0;
        }

        float SingleWaveHeight(float2 uv, float2 dir, float speed, float ampl, float t)
        {
            float d = dot(uv, dir);
            float ph = d * 10.0 + t * speed;
            float h = sin(ph) * 0.5 + 0.5;
            h = pow(h, 2.0);
            h = h * 2.0 - 1.0;
            return h * ampl;
        }

        float WaveHeight(float2 uv, float t, int num)
        {
            uv *= 1.6;
            float h = 0.0;
            float w = 1.0;
            float tw = 0.0;
            float s = 1.0;
            const float phBase = 0.2;

            [loop]
            for (int i = 0; i < num; i++)
            {
                float rand = hash11((float)i) * 2.0 - 1.0;
                float ph = phBase + rand * 0.75 * PI22;
                float2 dir = float2(sin(ph), cos(ph));
                h += SingleWaveHeight(uv, dir, 1.0 + s * 0.05, w, t);
                tw += w;
                const float scale = 1.0812;
                w /= scale;
                uv *= scale;
                s *= scale;
            }

            h /= max(tw, 1e-4);
            h = WAVE_BASE_HEIGHT + WAVE_MAX_AMPLITUDE * h;
            return h;
        }

        void RORD(float2 uv, out float3 ro, out float3 rd, float t)
        {
            float rotPh = _CameraOrbit;
            float y = _CameraHeight;
            float rad = _CameraRadius;
            ro = float3(sin(rotPh), y, cos(rotPh)) * rad;
            float3 lookAt = float3(0, 0, 0);
            float3 cf = normalize(lookAt - ro);
            float3 cr = normalize(cross(cf, float3(0, 1, 0)));
            float3 cu = normalize(cross(cr, cf));
            rd = normalize(uv.x * cr + uv.y * cu + cf);
        }

        float4 mod289(float4 x) { return x - floor(x / 289.0) * 289.0; }
        float4 permute(float4 x) { return mod289((x * 34.0 + 1.0) * x); }

        float4 snoise(float3 v)
        {
            const float2 C = float2(1.0 / 6.0, 1.0 / 3.0);
            float3 i = floor(v + dot(v, C.yyy));
            float3 x0 = v - i + dot(i, C.xxx);
            float3 g = step(x0.yzx, x0.xyz);
            float3 l = 1.0 - g;
            float3 i1 = min(g.xyz, l.zxy);
            float3 i2 = max(g.xyz, l.zxy);
            float3 x1 = x0 - i1 + C.x;
            float3 x2 = x0 - i2 + C.y;
            float3 x3 = x0 - 0.5;
            float4 p = permute(
                permute(permute(i.z + float4(0.0, i1.z, i2.z, 1.0)) + i.y + float4(0.0, i1.y, i2.y, 1.0)) + i.x +
                float4(0.0, i1.x, i2.x, 1.0));
            float4 j = p - 49.0 * floor(p / 49.0);
            float4 x_ = floor(j / 7.0);
            float4 y_ = floor(j - 7.0 * x_);
            float4 x = (x_ * 2.0 + 0.5) / 7.0 - 1.0;
            float4 y = (y_ * 2.0 + 0.5) / 7.0 - 1.0;
            float4 h = 1.0 - abs(x) - abs(y);
            float4 b0 = float4(x.xy, y.xy);
            float4 b1 = float4(x.zw, y.zw);
            float4 s0 = floor(b0) * 2.0 + 1.0;
            float4 s1 = floor(b1) * 2.0 + 1.0;
            float4 sh = -step(h, 0.0);
            float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
            float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
            float3 g0 = float3(a0.xy, h.x);
            float3 g1 = float3(a0.zw, h.y);
            float3 g2 = float3(a1.xy, h.z);
            float3 g3 = float3(a1.zw, h.w);
            float4 m = max(0.6 - float4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.0);
            float4 m2 = m * m;
            float4 m3 = m2 * m;
            float4 m4 = m2 * m2;
            float3 grad = -6.0 * m3.x * x0 * dot(x0, g0) + m4.x * g0 + -6.0 * m3.y * x1 * dot(x1, g1) + m4.y * g1 + -6.0
                * m3.z * x2 * dot(x2, g2) + m4.z * g2 + -6.0 * m3.w * x3 * dot(x3, g3) + m4.w * g3;
            float4 px = float4(dot(x0, g0), dot(x1, g1), dot(x2, g2), dot(x3, g3));
            return lerp(42.0, 0.0, gCausticNoiseBlur) * float4(grad, dot(m4, px));
        }

        float WaterHeight(float3 p, int waveCount) { return WaveHeight(p.xz * 0.1, _STTime, waveCount) + OCEAN_HEIGHT; }

        float GroundHeight(float3 p)
        {
            float h = 0.0;
            float tw = 0.0;
            float w = 1.0;
            p *= 0.2;
            p.xz += float2(-1.25, 0.35);
            [loop]
            for (int i = 0; i < 2; i++)
            {
                h += w * sin(p.x) * sin(p.z);
                const float s = 1.173;
                tw += w;
                p *= s;
                p.xz += float2(2.373, 0.977);
                w /= s;
            }
            h /= max(tw, 1e-4);
            return -0.2 + 1.65 * h;
        }

        float sdOcean(float3 p) { return (p.y - WaterHeight(p, NUM_WAVES)) * 0.75; }
        float sdOcean_Levels(float3 p, int waveCount) { return (p.y - WaterHeight(p, waveCount)) * 0.75; }

        float map(float3 p, bool includeWater)
        {
            float hGround = GroundHeight(p);
            float dGround = (p.y - hGround) * 0.9;
            float d = dGround;
            gMat = MAT_GROUND;
            if (includeWater)
            {
                float dOcean = sdOcean(p);
                if (dOcean < d) gMat = MAT_OCEAN;
                d = min(d, dOcean);
            }
            return d;
        }

        float RM(float3 ro, float3 rd)
        {
            float t = 0.0;
            float s = 1.0;
            [loop]
            for (int i = 0; i < STEPS; i++)
            {
                float d = map(ro + t * rd, true);
                if (d < 0.001) return t;
                t += d * s;
                s *= 1.02;
            }
            return -t;
        }

        float RM_Ground(float3 ro, float3 rd)
        {
            float t = 0.0;
            [loop]
            for (int i = 0; i < STEPS_GROUND; i++)
            {
                float d = map(ro + t * rd, false);
                if (d < 0.001) return t;
                t += d;
            }
            return -t;
        }

        float3 Normal(float3 p)
        {
            const float h = 0.001;
            const float2 k = float2(1, -1);
            return normalize(
                k.xyy * map(p + k.xyy * h, true) + k.yyx * map(p + k.yyx * h, true) + k.yxy * map(p + k.yxy * h, true) +
                k.xxx * map(p + k.xxx * h, true));
        }

        float3 WaveNormal_Levels(float3 p, int levels)
        {
            const float h = 0.001;
            const float2 k = float2(1, -1);
            return normalize(
                k.xyy * sdOcean_Levels(p + k.xyy * h, levels) + k.yyx * sdOcean_Levels(p + k.yyx * h, levels) + k.yxy *
                sdOcean_Levels(p + k.yxy * h, levels) + k.xxx * sdOcean_Levels(p + k.xxx * h, levels));
        }

        float water_caustics(float3 pos)
        {
            float4 n = snoise(pos);
            pos -= 0.07 * n.xyz;
            pos *= 1.62;
            n = snoise(pos);
            pos -= 0.07 * n.xyz;
            n = snoise(pos);
            pos -= 0.07 * n.xyz;
            n = snoise(pos);
            return n.w;
        }

        void DarkenGround(inout float3 col, float3 groundPos, float oceanHeight, out float wetness)
        {
            wetness = 1.0 - smoothstep(0.05, 0.2, groundPos.y - oceanHeight - 0.3);
            col = lerp(col, col * float3(0.95, 0.92, 0.85) * 0.8, wetness);
        }

        float3 SampleEnv(float3 dir)
        {
            dir = normalize(dir);
            float2 uv;
            uv.x = AtanGLSL(dir.z, dir.x) / (2.0 * PI) + 0.5;
            uv.y = SafeAsin(clamp(dir.y, -0.999, 0.999)) / PI + 0.5;
            return SAMPLE_TEXTURE2D_LOD(_Channel0, sampler_Channel0, uv, 0).rgb;
        }

        float3 ReflectionCol(float3 refl, float fresnel)
        {
            float spec = pow(max(0.0, dot(refl, -LD)), 256.0);
            float3 col = spec.xxx;
            col += fresnel * SampleEnv(refl) * 0.4;
            return col;
        }

        float Fresnel(float3 rd, float3 nor)
        {
            float f = 1.0 - abs(dot(nor, rd));
            return pow(f, 6.0);
        }

        void ApplyFog(inout float3 col, float t, float3 ro, float3 rd)
        {
            // same as common's default path when APPLY_HEIGHT_FOG disabled
        }

        float3 Render(float t, float3 ro, float3 rd)
        {
            if (t < 0.0)
            {
                float3 col = float3(0.35, 0.62, 0.9);
                col = lerp(col, float3(1.0, 1.0, 1.0), max(0.0, (1.0 - rd.y) * 0.3));
                float sunDot = tanh(pow(max(0.0, dot(rd, -LD)), 6.0));
                col += sunDot * float3(1, 0.8, 0.7);
                ApplyFog(col, 10000.0, ro, rd);
                return col;
            }

            float3 p = ro + t * rd;
            float3 pGround;
            float3 col = float3(0.9, 0.85, 0.7);

            if (gMat == MAT_OCEAN)
            {
                float hGround = GroundHeight(p);
                float nearShoreAlpha = 1.0 - smoothstep(0.5, -0.2, hGround - OCEAN_HEIGHT);
                float3 nor = normalize(lerp(Normal(p), float3(0, 1, 0), nearShoreAlpha * 0.9));
                float3 refl = reflect(rd, nor);
                float3 refr = refract(rd, nor, 1.0 / 1.2);
                if (all(refr == 0)) refr = refl;

                float tGround = RM_Ground(p, refr);
                if (tGround < 0.0) tGround = 4.0;
                pGround = p + tGround * refr;

                float fresnel = Fresnel(rd, nor);
                float3 norSubsurf = WaveNormal_Levels(p, NUM_WAVES / 3);
                float3 ldSubsurf = LD * float3(1, -1, 1);
                float subsurf = max(0.0, max(0.0, dot(rd, -ldSubsurf)) * dot(norSubsurf, ldSubsurf));
                subsurf = pow(subsurf, 2.0) * (1.0 - fresnel) * 0.5;

                float wetness;
                DarkenGround(col, pGround, OCEAN_HEIGHT, wetness);

                float3 transmittance = exp(-tGround * WATER_ABSORP / WATER_COL);
                float3 causticPos = pGround * 2.0 + float3(0, _STTime * 0.15, 0);
                float causticAlpha = 1.0 - saturate1(exp(-tGround * 2.0));
                gCausticNoiseBlur = 1.0 - min(1.0, causticAlpha * 2.0);
                float3 o = float3(1.0, 0.0, 1.0) * 0.02;
                float3 caustics;
                caustics.x = lerp(water_caustics(causticPos + o), water_caustics(causticPos + o + 1.0), 0.5);
                caustics.y = lerp(water_caustics(causticPos + o * 4.0), water_caustics(causticPos + o + 1.0), 0.5);
                caustics.z = lerp(water_caustics(causticPos + o * 6.0), water_caustics(causticPos + o + 1.0), 0.5);
                caustics = exp(caustics * 4.0 - 1.0) * causticAlpha;

                col += caustics;
                col *= transmittance;
                col += tGround * exp(-tGround * WATER_ABSORP) * WATER_COL * 0.3;
                col += subsurf * SUBSURF_COL;
                col += ReflectionCol(refl, fresnel);
            }
            else
            {
                pGround = p;
                float wetness;
                DarkenGround(col, pGround, OCEAN_HEIGHT, wetness);
                float3 nor = Normal(p);
                float3 refl = reflect(rd, nor);
                col += wetness * ReflectionCol(refl, Fresnel(rd, nor));
            }

            ApplyFog(col, t, ro, rd);
            return col;
        }

        float4 Frag(Varyings i) : SV_Target
        {
            float2 fragCoord = i.uv * _STResolution.xy;
            float2 uv = (2.0 * fragCoord - _STResolution.xy) / _STResolution.y;
            float3 ro, rd;
            RORD(uv, ro, rd, _STTime);
            float d = RM(ro, rd);
            float3 col = Render(d, ro, rd);
            col = pow(col, 1.0 / 2.2);
            return float4(col, 1.0);
        }


        float4 RenderShadertoy(float2 fragCoord)
        {
            Varyings input = (Varyings)0;
            input.uv = fragCoord / _STResolution.xy;
            return Frag(input);
        }

        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = input.uv;
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "WaterPreview"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LightMode" = "DepthOnly"
            }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Assets/unity-shadertoy-validation/Common/Shaders/ShadertoyDepthOnlyPass.hlsl"
            ENDHLSL
        }
    }
}
