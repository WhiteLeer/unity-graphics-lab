Shader "SurfaceLab/Crystal/VolumePreviewLike"
{
    Properties
    {
        [MainColor] _BaseColor("主体颜色", Color) = (0.86, 0.9, 1, 1)
        _ShadowTint("暗部颜色", Color) = (0.08, 0.1, 0.16, 1)
        _EdgeTint("边缘冷光", Color) = (0.52, 0.88, 1, 1)
        _GlowTint("内部辉光", Color) = (0.98, 0.72, 1, 1)
        _DensityTex("三维噪声图", 3D) = "" {}

        _NoiseFrequency("噪声频率", Range(0.25, 8.0)) = 2.4
        _NoiseAmount("噪声扰动", Range(0.0, 0.25)) = 0.06
        _ShapeMode("形状模式", Range(0, 3)) = 0
        _ShapeBlend("形体圆润", Range(0.0, 0.2)) = 0.03
        _SurfaceOffset("表面偏移", Range(-0.1, 0.1)) = 0.0

        _RefractionIndex("折射率", Range(1.01, 2.0)) = 1.18
        _InternalDistance("内部取样距离", Range(0.2, 4.0)) = 1.8
        _InternalStep("内部取样步长", Range(0.02, 0.5)) = 0.12
        _Absorption("内部吸收", Range(0.2, 4.0)) = 1.15
        _GlowStrength("辉光强度", Range(0.0, 8.0)) = 2.1
        _FresnelPower("边缘亮度范围", Range(0.2, 8.0)) = 4.4
        _SpecularPower("高光锐利度", Range(8.0, 256.0)) = 84
        _SpecularIntensity("高光强度", Range(0.0, 8.0)) = 2.2

        _TraceSteps("主追踪步数", Range(24, 256)) = 128
        _HitEpsilon("命中精度", Range(0.001, 0.03)) = 0.004
        _MaxDistance("最大距离", Range(0.5, 4.0)) = 2.0
        _NormalStep("法线精度", Range(0.001, 0.03)) = 0.01
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "CrystalVolumeObject"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ShadowTint;
                float4 _EdgeTint;
                float4 _GlowTint;
                float _NoiseFrequency;
                float _NoiseAmount;
                float _ShapeMode;
                float _ShapeBlend;
                float _SurfaceOffset;
                float _RefractionIndex;
                float _InternalDistance;
                float _InternalStep;
                float _Absorption;
                float _GlowStrength;
                float _FresnelPower;
                float _SpecularPower;
                float _SpecularIntensity;
                float _TraceSteps;
                float _HitEpsilon;
                float _MaxDistance;
                float _NormalStep;
                float _PreviewPitch;
                float _PreviewYaw;
            CBUFFER_END

            TEXTURE3D(_DensityTex);
            SAMPLER(sampler_DensityTex);

            float3 _VolumeLightPositionWS;
            float4 _VolumeLightColor;
            float _VolumeLightIntensity;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionOS = input.positionOS.xyz;
                return o;
            }

            bool IntersectUnitBox(float3 ro, float3 rd, out float tNear, out float tFar)
            {
                float3 safeSign = float3(
                    rd.x >= 0.0 ? 1.0 : -1.0,
                    rd.y >= 0.0 ? 1.0 : -1.0,
                    rd.z >= 0.0 ? 1.0 : -1.0
                );
                float3 safeRd = safeSign * max(abs(rd), 1e-5);
                float3 invRd = 1.0 / safeRd;
                float3 t0 = (-0.5 - ro) * invRd;
                float3 t1 = (0.5 - ro) * invRd;
                float3 tMin = min(t0, t1);
                float3 tMax = max(t0, t1);
                tNear = max(max(tMin.x, tMin.y), tMin.z);
                tFar = min(min(tMax.x, tMax.y), tMax.z);
                return tFar >= max(tNear, 0.0);
            }

            float SampleNoise(float3 positionOS)
            {
                float3 uvw = frac(positionOS * _NoiseFrequency + 0.5);
                return SAMPLE_TEXTURE3D(_DensityTex, sampler_DensityTex, uvw).r;
            }

            float SdSphere(float3 p, float r)
            {
                return length(p) - r;
            }

            float SdBox(float3 p, float3 b)
            {
                float3 q = abs(p) - b;
                return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
            }

            float SdCapsule(float3 p, float3 a, float3 b, float r)
            {
                float3 pa = p - a;
                float3 ba = b - a;
                float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-5));
                return length(pa - ba * h) - r;
            }

            float2 Rot2(float2 p, float a)
            {
                float s = sin(a);
                float c = cos(a);
                return float2(c * p.x + s * p.y, -s * p.x + c * p.y);
            }

            float3 RotatePreviewToVolume(float3 p)
            {
                p.xz = Rot2(p.xz, radians(_PreviewYaw));
                p.yz = Rot2(p.yz, radians(-_PreviewPitch));
                return p;
            }

            struct CrystalState
            {
                float3 cp, cn, cr, ro, rd, ss, oc, cc, gl, vb;
                float4 fc;
                float tt, cd, sd, io, oa, td, maxDistance;
                int es, ec;
            };

            CrystalState InitState()
            {
                CrystalState st;
                st.cp = 0.0;
                st.cn = 0.0;
                st.cr = 0.0;
                st.ro = 0.0;
                st.rd = 0.0;
                st.ss = 0.0;
                st.oc = 0.0;
                st.cc = 0.0;
                st.gl = 0.0;
                st.vb = 0.0;
                st.fc = 0.0;
                st.tt = 0.0;
                st.cd = 0.0;
                st.sd = 0.0;
                st.io = max(_RefractionIndex, 1.001);
                st.oa = 0.0;
                st.td = 0.0;
                st.maxDistance = 0.0;
                st.es = 0;
                st.ec = 0;
                return st;
            }

            float3 SafeNormalize3(float3 v)
            {
                float lenSq = dot(v, v);
                if (lenSq <= 1e-12)
                {
                    return 0.0.xxx;
                }

                return v * rsqrt(lenSq);
            }

            float3 ReflectGLSL(float3 i, float3 n)
            {
                return i - 2.0 * dot(n, i) * n;
            }

            float3 RefractGLSL(float3 i, float3 n, float eta)
            {
                float d = dot(n, i);
                float k = 1.0 - eta * eta * (1.0 - d * d);
                if (k < 0.0)
                {
                    return 0.0.xxx;
                }

                return eta * i - (eta * d + sqrt(k)) * n;
            }

            float3 SanitizeColor(float3 c)
            {
                c = any(isnan(c)) ? 0.0.xxx : c;
                c = any(isinf(c)) ? 0.0.xxx : c;
                return max(c, 0.0);
            }

            float3 CrystalLattice(float3 p, int iter, float angleDeg)
            {
                float angle = radians(angleDeg);
                [loop]
                for (int i = 0; i < 9; i++)
                {
                    if (i >= iter)
                    {
                        break;
                    }

                    p.xy = Rot2(p.xy, angle);
                    p.yz = abs(p.yz) - 1.0;
                    p.xz = Rot2(p.xz, -angle);
                }
                return p;
            }

            float3 StaticCrystalFoldA(float3 p)
            {
                p.xz = Rot2(p.xz, radians(24.0));
                p.xy = Rot2(p.xy, radians(-18.0));
                return CrystalLattice(p * 1.34, 5, 41.0);
            }

            float3 StaticCrystalFoldB(float3 p)
            {
                p.xy = Rot2(p.xy, radians(33.0));
                p.yz = Rot2(p.yz, radians(-27.0));
                return CrystalLattice(p * 1.08, 4, 36.0);
            }

            float3 StaticCrystalFoldC(float3 p)
            {
                p.xz = Rot2(p.xz, radians(-31.0));
                p.xy = Rot2(p.xy, radians(29.0));
                return CrystalLattice(p * 0.9, 3, 52.0);
            }

            float3 StaticCrystalFoldD(float3 p)
            {
                p.xy = Rot2(p.xy, radians(-44.0));
                p.yz = Rot2(p.yz, radians(15.0));
                return CrystalLattice(p * 1.56, 6, 28.0);
            }

            float BaseShapeSdf(float3 p)
            {
                int shapeMode = (int)round(_ShapeMode);
                if (shapeMode == 1)
                {
                    return SdBox(p, float3(0.24, 0.24, 0.24)) - _ShapeBlend;
                }
                if (shapeMode == 2)
                {
                    return SdCapsule(p, float3(0.0, -0.22, 0.0), float3(0.0, 0.22, 0.0), 0.18);
                }
                if (shapeMode == 3)
                {
                    float3 q = p;
                    q.y *= 1.25;
                    q.z *= 0.78;
                    float main = SdSphere(q, 0.30);
                    float bumpA = SdSphere(q - float3(-0.12, 0.08, 0.02), 0.16);
                    float bumpB = SdSphere(q - float3(0.11, -0.05, -0.07), 0.14);
                    return min(main, min(bumpA, bumpB)) - _ShapeBlend;
                }
                return SdSphere(p, 0.30);
            }

            float CrystalShapeSdfLocal(float3 p)
            {
                int shapeMode = (int)round(_ShapeMode);
                if (shapeMode == 1)
                {
                    return SdBox(p, float3(0.28, 0.28, 0.28));
                }

                if (shapeMode == 2)
                {
                    return SdCapsule(p, float3(0.0, -0.24, 0.0), float3(0.0, 0.24, 0.0), 0.16);
                }

                if (shapeMode == 3)
                {
                    float3 q = p;
                    q.y *= 1.18;
                    q.z *= 0.84;
                    float main = SdSphere(q, 0.31);
                    float bumpA = SdSphere(q - float3(-0.12, 0.08, 0.02), 0.17);
                    float bumpB = SdSphere(q - float3(0.11, -0.06, -0.07), 0.15);
                    return min(main, min(bumpA, bumpB));
                }

                return SdSphere(p, 0.30);
            }

            float CrystalShapeSdf(float3 p)
            {
                return CrystalShapeSdfLocal(RotatePreviewToVolume(p));
            }

            float Field(float3 p)
            {
                float noise = SampleNoise(p) - 0.5;
                return BaseShapeSdf(p) - noise * _NoiseAmount - _SurfaceOffset;
            }

            float CrystalDistance(float3 p, out float3 crystalGlow);
            float CrystalDistance(float3 p);
            float CrystalInteriorFacetValue(float3 p);
            float CrystalInteriorFacetValueLocal(float3 p);
            float CrystalOuterShellDistance(float3 p);

            bool TraceVolume(float3 roOS, float3 rdOS, out float3 hitOS, out float3 hitWS)
            {
                float tNear;
                float tFar;
                if (!IntersectUnitBox(roOS, rdOS, tNear, tFar))
                {
                    hitOS = 0.0;
                    hitWS = 0.0;
                    return false;
                }

                float t = max(tNear, 0.0);
                float endT = min(tFar, t + _MaxDistance);

                [loop]
                for (int i = 0; i < 256; i++)
                {
                    if (i >= (int)_TraceSteps || t > endT)
                    {
                        break;
                    }

                    float3 p = roOS + rdOS * t;
                    float d = Field(p);
                    if (abs(d) <= _HitEpsilon)
                    {
                        hitOS = p;
                        hitWS = TransformObjectToWorld(hitOS);
                        return true;
                    }

                    t += max(abs(d), _HitEpsilon * 0.5);
                }

                hitOS = 0.0;
                hitWS = 0.0;
                return false;
            }

            float3 VolumeNormal(float3 p)
            {
                float e = _NormalStep;
                float3 n = float3(
                    CrystalDistance(p + float3(e, 0.0, 0.0)) - CrystalDistance(p - float3(e, 0.0, 0.0)),
                    CrystalDistance(p + float3(0.0, e, 0.0)) - CrystalDistance(p - float3(0.0, e, 0.0)),
                    CrystalDistance(p + float3(0.0, 0.0, e)) - CrystalDistance(p - float3(0.0, 0.0, e))
                );
                return normalize(n);
            }

            float CrystalOuterShellDistance(float3 p)
            {
                p = RotatePreviewToVolume(p);
                return CrystalShapeSdfLocal(p) - _SurfaceOffset;
            }

            float PreviewCrystalFieldLocal(float3 p, out float3 crystalGlow)
            {
                float3 warp = float3(
                    sin(p.y * 6.0 + p.z * 4.0),
                    sin(p.z * 5.0 - p.x * 4.5),
                    sin(p.x * 5.5 + p.y * 3.5)
                ) * 0.024;
                p += warp;
                float noise = (SampleNoise(p * 0.88) - 0.5) * (_NoiseAmount * 0.025);

                float3 qA = StaticCrystalFoldA(p * 1.34 + float3(0.00, 0.00, 0.00));
                float rawA = SdBox(qA, float3(1.00, 1.00, 1.00)) - 0.010 - noise;
                float shellA = abs(rawA) - 0.00065;

                float3 qB = StaticCrystalFoldB(p * 1.12 + float3(0.08, -0.03, 0.05));
                float rawB = SdBox(qB, float3(0.94, 1.06, 0.88)) - 0.008 + noise * 0.45;
                float shellB = abs(rawB) - 0.00072;

                float3 qC = StaticCrystalFoldC(p * 1.48 + float3(-0.05, 0.06, -0.04));
                float rawC = SdBox(qC, float3(1.06, 0.88, 0.98)) - 0.007 - noise * 0.35;
                float shellC = abs(rawC) - 0.00078;

                float3 qD = StaticCrystalFoldD(p * 1.06 + float3(0.03, 0.08, -0.06));
                float rawD = SdBox(qD, float3(0.86, 1.14, 0.84)) - 0.007 + noise * 0.28;
                float shellD = abs(rawD) - 0.00074;

                float3 qE = StaticCrystalFoldA(p * 1.72 + float3(-0.10, 0.04, 0.09));
                float rawE = SdBox(qE, float3(0.82, 0.92, 0.78)) - 0.006 + noise * 0.18;
                float shellE = abs(rawE) - 0.00058;

                float3 qF = StaticCrystalFoldC(p * 1.96 + float3(0.09, -0.08, 0.02));
                float rawF = SdBox(qF, float3(0.76, 0.84, 0.74)) - 0.005 - noise * 0.14;
                float shellF = abs(rawF) - 0.00052;

                float shellField = min(min(min(shellA, shellB), min(shellC, shellD)), min(shellE, shellF));
                float3 glowVec = max(qA * qA + qB * qB * 0.74 + qC * qC * 0.58 + qD * qD * 0.48 + qE * qE * 0.36 + qF * qF * 0.28, 1e-6);
                float3 tintA = float3(1.00, 0.74, 0.92);
                float3 tintB = float3(0.78, 0.90, 1.00);
                float3 tintC = float3(1.00, 0.82, 0.72);
                float3 tintD = float3(0.88, 0.76, 1.00);
                float3 tintE = float3(0.72, 0.96, 1.00);
                float3 tintF = float3(1.00, 0.66, 0.84);
                crystalGlow = (
                    exp(-abs(rawA) * 54.0) * SafeNormalize3(glowVec) * tintA * 0.0019 +
                    exp(-abs(rawB) * 46.0) * SafeNormalize3(glowVec.zxy) * tintB * 0.0016 +
                    exp(-abs(rawC) * 38.0) * SafeNormalize3(glowVec.yzx) * tintC * 0.0014 +
                    exp(-abs(rawD) * 50.0) * SafeNormalize3(glowVec.xzy) * tintD * 0.0015 +
                    exp(-abs(rawE) * 62.0) * SafeNormalize3(glowVec * 1.2) * tintE * 0.0012 +
                    exp(-abs(rawF) * 68.0) * SafeNormalize3(glowVec.yxz) * tintF * 0.0010
                );
                return shellField;
            }

            float CrystalDistance(float3 p, out float3 crystalGlow)
            {
                p = RotatePreviewToVolume(p);
                float outerShape = CrystalShapeSdfLocal(p);
                float outerShell = outerShape > 0.0
                    ? (outerShape - _SurfaceOffset)
                    : (abs(outerShape - _SurfaceOffset) - 0.0015);

                float interiorField = PreviewCrystalFieldLocal(p, crystalGlow);
                float interiorMask = smoothstep(-0.22, -0.03, -outerShape);
                float clippedInterior = max(outerShape + 0.006, interiorField);
                crystalGlow *= interiorMask * saturate(0.012 - abs(interiorField)) / 0.012;
                return min(abs(outerShell) - 0.0015, clippedInterior);
            }

            float CrystalDistance(float3 p)
            {
                float3 crystalGlow;
                return CrystalDistance(p, crystalGlow);
            }

            float CrystalInteriorFacetValue(float3 p)
            {
                p = RotatePreviewToVolume(p);
                return CrystalInteriorFacetValueLocal(p);
            }

            float CrystalInteriorFacetValueLocal(float3 p)
            {
                float3 glow;
                return PreviewCrystalFieldLocal(p, glow);
            }

            void ResolveCrystalHit(float sd, inout CrystalState st)
            {
                if (abs(sd) < _HitEpsilon)
                {
                    st.oc = 1.0.xxx;
                    st.io = max(_RefractionIndex, 1.001);
                    st.oa = 0.0;
                    st.ss = 0.0.xxx;
                    st.vb = float3(0.0, 10.0, 2.8);
                    st.ec = 2;
                }
            }

            void TraceCrystal(inout CrystalState st, bool includeInteriorShells)
            {
                st.vb.x = 0.0;
                st.cd = 0.0;
                st.gl = 0.0.xxx;

                [loop]
                for (int i = 0; i < 256; i++)
                {
                    if (i >= (int)_TraceSteps)
                    {
                        break;
                    }

                    float3 samplePos = st.ro + st.rd * st.cd;

                    if (!includeInteriorShells)
                    {
                        st.sd = CrystalOuterShellDistance(samplePos);
                        st.cd += max(st.sd, _HitEpsilon * 0.5);
                        st.td += st.sd;
                        if (st.sd < _HitEpsilon || st.cd > st.maxDistance)
                        {
                            ResolveCrystalHit(st.sd, st);
                            break;
                        }
                        continue;
                    }

                    float outerShape = CrystalShapeSdf(samplePos) - _SurfaceOffset;
                    if (outerShape > _HitEpsilon)
                    {
                        st.cd = st.maxDistance + _HitEpsilon;
                        break;
                    }

                    float3 localGlow = 0.0.xxx;
                    st.sd = CrystalInteriorFacetValue(samplePos);
                    PreviewCrystalFieldLocal(RotatePreviewToVolume(samplePos), localGlow);
                    st.gl += localGlow * smoothstep(-0.22, -0.03, -outerShape);
                    if (abs(st.sd) < _HitEpsilon)
                    {
                        ResolveCrystalHit(st.sd, st);
                        break;
                    }

                    st.cd += max(abs(st.sd) * 0.45, max(_InternalStep * 0.06, _HitEpsilon * 1.25));
                    st.td += abs(st.sd);
                    if (st.cd > st.maxDistance)
                    {
                        break;
                    }
                }
            }

            void NormalCrystal(inout CrystalState st, bool includeInteriorShells)
            {
                float3 kx = st.cp - float3(_NormalStep, 0.0, 0.0);
                float3 ky = st.cp - float3(0.0, _NormalStep, 0.0);
                float3 kz = st.cp - float3(0.0, 0.0, _NormalStep);
                float center = includeInteriorShells ? CrystalInteriorFacetValue(st.cp) : CrystalOuterShellDistance(st.cp);
                float dx = includeInteriorShells ? CrystalInteriorFacetValue(kx) : CrystalOuterShellDistance(kx);
                float dy = includeInteriorShells ? CrystalInteriorFacetValue(ky) : CrystalOuterShellDistance(ky);
                float dz = includeInteriorShells ? CrystalInteriorFacetValue(kz) : CrystalOuterShellDistance(kz);
                float3 outerNormal = SafeNormalize3(center - float3(dx, dy, dz));

                if (!includeInteriorShells)
                {
                    st.cn = outerNormal;
                    return;
                }

                st.cn = outerNormal;
            }

            void ShadeCrystal(inout CrystalState st)
            {
                st.cc = _ShadowTint.rgb + length(pow(abs(st.rd + float3(0.0, 0.5, 0.0)), 3.0)) * 0.30 + st.gl;
                float3 l = SafeNormalize3(float3(0.9, 0.7, 0.5));
                if (st.cd > st.maxDistance)
                {
                    st.oa = 1.0;
                    return;
                }

                float df = saturate(length(st.cn * l));
                float hue = saturate(st.cn.x * 0.5 + 0.5);
                float height = saturate(st.cn.y * 0.5 + 0.5);
                float3 crystalTint = lerp(_GlowTint.rgb, _EdgeTint.rgb, hue);
                crystalTint = lerp(crystalTint, _BaseColor.rgb, height * 0.42);
                float3 fr = pow(1.0 - df, 3.0) * lerp(st.cc, lerp(_EdgeTint.rgb, float3(1.0, 1.0, 1.0), 0.2), 0.66);
                float sp = pow(saturate(1.0 - length(cross(st.cr, st.cn * l))), 1.2) * (_SpecularIntensity * 0.18);
                float ao = min(CrystalOuterShellDistance(st.cp + st.cn * 0.24) - 0.24, 0.24) * 0.18;
                float3 interiorGlow = st.gl * (_GlowStrength * 1.55);
                st.cc = lerp(st.oc * (crystalTint * (df + 0.10) + fr + st.ss) + fr + sp + ao + interiorGlow, crystalTint, st.vb.x * 0.10);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 roOS = TransformWorldToObject(_WorldSpaceCameraPos);
                float2 uv = input.positionCS.xy / _ScaledScreenParams.xy;
                float2 ndc = uv * 2.0 - 1.0;
                ndc.y *= -1.0;

                float4 clipPos = float4(ndc, 1.0, 1.0);
                float4 viewPos = mul(unity_CameraInvProjection, clipPos);
                float3 rdVS = SafeNormalize3(viewPos.xyz / max(viewPos.w, 1e-5));
                float3 rdWS = SafeNormalize3(mul((float3x3)UNITY_MATRIX_I_V, rdVS));
                float3 rdOS = SafeNormalize3(TransformWorldToObjectDir(rdWS));

                float enterT;
                float exitT;
                if (!IntersectUnitBox(roOS, rdOS, enterT, exitT))
                {
                    clip(-1.0);
                    return 0.0;
                }

                CrystalState st = InitState();
                st.tt = _Time.y + 25.0;
                st.ro = roOS + rdOS * max(enterT, 0.0);
                st.rd = rdOS;
                st.maxDistance = min(max(exitT - max(enterT, 0.0), _HitEpsilon * 2.0), _MaxDistance);

                [loop]
                for (int bounce = 0; bounce < 25; bounce++)
                {
                    bool includeInteriorShells = bounce > 0;
                    TraceCrystal(st, includeInteriorShells);
                    st.cp = st.ro + st.rd * st.cd;

                    if (st.cd > st.maxDistance)
                    {
                        break;
                    }

                    NormalCrystal(st, includeInteriorShells);
                    st.ro = st.cp - st.cn * 0.01;
                    st.cr = RefractGLSL(st.rd, st.cn, (bounce % 2 == 0) ? (1.0 / st.io) : st.io);
                    if (dot(st.cr, st.cr) <= 1e-12 && st.es <= 0)
                    {
                        st.cr = ReflectGLSL(st.rd, st.cn);
                        st.es = st.ec;
                    }

                    if ((max(st.es, 0) % 3) == 0 && st.cd < st.maxDistance)
                    {
                        st.rd = SafeNormalize3(st.cr);
                    }

                    st.es--;
                    st.oa = saturate(0.018 + length(st.gl) * 0.12 + bounce * 0.0045);

                    ShadeCrystal(st);
                    st.fc += float4(SanitizeColor(st.cc) * st.oa, st.oa) * (1.0 - st.fc.a);
                    if (st.fc.a >= 0.98)
                    {
                        break;
                    }
                }

                if (st.fc.a <= 1e-4)
                {
                    clip(-1.0);
                    return 0.0;
                }

                return float4(SanitizeColor(st.fc.rgb / max(st.fc.a, 1e-4)), 1.0);
            }
            ENDHLSL
        }
    }
}
