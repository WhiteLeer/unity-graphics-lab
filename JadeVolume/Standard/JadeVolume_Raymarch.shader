Shader "SurfaceLab/JadeVolume/Raymarch"
{
    Properties
    {
        [MainColor] _BaseColor("玉石主体颜色", Color) = (0.75, 0.9, 0.35, 1)
        _AmbientTint("暗部透光颜色", Color) = (0.1, 0.06, 0.035, 1)
        _SkyTint("边缘冷光颜色", Color) = (0.5, 0.65, 0.8, 1)
        _LightPositionOS("内部光源位置", Vector) = (50, 20, 10, 0)

        _FieldScale("体积细节缩放", Range(1, 128)) = 28
        _FieldCenter("体积中心偏移", Vector) = (0, 0, 0, 0)
        _HeightAmplitude("波峰高度", Range(0, 16)) = 6
        _BottomDepth("底部厚度", Range(0.5, 16)) = 7
        _FootprintCenter("体积中心", Vector) = (0, 16, 0, 0)
        _FootprintInner("核心范围", Range(0, 20)) = 5
        _FootprintOuter("体积范围", Range(1, 40)) = 20

        _PrimaryNoiseScale("大波纹密度", Range(0.01, 1)) = 0.07
        _SecondaryNoiseScale("小波纹密度", Range(0.01, 1)) = 0.173
        _PrimaryNoiseSpeed("大波纹速度", Range(0, 2)) = 0.5
        _SecondaryNoiseSpeed("小波纹速度", Range(0, 2)) = 0.639

        _FresnelPower("边缘亮度范围", Range(0.1, 8)) = 5
        _SpecularMultiplier("高光强度", Range(0, 64)) = 25
        _SpecularRoughness("高光柔和度", Range(0.1, 8.0)) = 3

        _ScatterStrength("通透强度", Range(0, 64)) = 16
        _ScatterDistance("通透范围", Range(0.2, 8.0)) = 2.5
        _ScatterStep("通透细腻度", Range(0.02, 0.5)) = 0.2
        _ScatterIor("折射感", Range(1.01, 2.0)) = 1.5
        _ScatterBlend("通透混合", Range(0, 1)) = 0.7
        _ScatterBoost("通透提亮", Range(0, 8)) = 3.5
        _ScatterCurve("通透柔和", Range(0.1, 2.0)) = 0.6

        _PrimaryTraceSteps("主追踪步数", Range(32, 192)) = 150
        _PrimaryTraceHitDistance("主追踪命中精度", Range(0.002, 0.05)) = 0.01
        _PrimaryTraceMaxDistance("主追踪最大距离", Range(10, 120)) = 60
        _PrimaryTraceRefineSteps("边缘细化次数", Range(0, 6)) = 3

        _RaymarchSteps("体积精度", Range(24, 160)) = 96
        _HitDistance("表面精度", Range(0.001, 0.05)) = 0.01
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
            Name "JadeVolumeRaymarch"
            Tags
            {
                "LightMode" = "UniversalForward"
            }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _AmbientTint;
                float4 _SkyTint;
                float4 _LightPositionOS;
                float4 _FieldCenter;
                float4 _FootprintCenter;
                float _FieldScale;
                float _HeightAmplitude;
                float _BottomDepth;
                float _FootprintInner;
                float _FootprintOuter;
                float _PrimaryNoiseScale;
                float _SecondaryNoiseScale;
                float _PrimaryNoiseSpeed;
                float _SecondaryNoiseSpeed;
                float _FresnelPower;
                float _SpecularMultiplier;
                float _SpecularRoughness;
                float _ScatterStrength;
                float _ScatterDistance;
                float _ScatterStep;
                float _ScatterIor;
                float _ScatterBlend;
                float _ScatterBoost;
                float _ScatterCurve;
                float _PrimaryTraceSteps;
                float _PrimaryTraceHitDistance;
                float _PrimaryTraceMaxDistance;
                float _PrimaryTraceRefineSteps;
                float _RaymarchSteps;
                float _HitDistance;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
            };

            float3x3 RotateAxis(float3 v, float angle)
            {
                float c = cos(angle);
                float s = sin(angle);
                float3x3 m = float3x3(
                    c + (1.0 - c) * v.x * v.x, (1.0 - c) * v.x * v.y - s * v.z, (1.0 - c) * v.x * v.z + s * v.y,
                    (1.0 - c) * v.x * v.y + s * v.z, c + (1.0 - c) * v.y * v.y, (1.0 - c) * v.y * v.z - s * v.x,
                    (1.0 - c) * v.x * v.z - s * v.y, (1.0 - c) * v.y * v.z + s * v.x, c + (1.0 - c) * v.z * v.z
                );

                return transpose(m);
            }

            float3 Hash33(float3 p)
            {
                p = float3(
                    dot(p, float3(127.1, 311.7, 74.7)),
                    dot(p, float3(269.5, 183.3, 246.1)),
                    dot(p, float3(113.5, 271.9, 124.6))
                );
                return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
            }

            float4 Noised(float3 x)
            {
                float3 p = floor(x);
                float3 w = frac(x);
                float3 u = w * w * w * (w * (w * 6.0 - 15.0) + 10.0);
                float3 du = 30.0 * w * w * (w * (w - 2.0) + 1.0);

                float3 ga = Hash33(p + float3(0.0, 0.0, 0.0));
                float3 gb = Hash33(p + float3(1.0, 0.0, 0.0));
                float3 gc = Hash33(p + float3(0.0, 1.0, 0.0));
                float3 gd = Hash33(p + float3(1.0, 1.0, 0.0));
                float3 ge = Hash33(p + float3(0.0, 0.0, 1.0));
                float3 gf = Hash33(p + float3(1.0, 0.0, 1.0));
                float3 gg = Hash33(p + float3(0.0, 1.0, 1.0));
                float3 gh = Hash33(p + float3(1.0, 1.0, 1.0));

                float va = dot(ga, w - float3(0.0, 0.0, 0.0));
                float vb = dot(gb, w - float3(1.0, 0.0, 0.0));
                float vc = dot(gc, w - float3(0.0, 1.0, 0.0));
                float vd = dot(gd, w - float3(1.0, 1.0, 0.0));
                float ve = dot(ge, w - float3(0.0, 0.0, 1.0));
                float vf = dot(gf, w - float3(1.0, 0.0, 1.0));
                float vg = dot(gg, w - float3(0.0, 1.0, 1.0));
                float vh = dot(gh, w - float3(1.0, 1.0, 1.0));

                float value = va + u.x * (vb - va) + u.y * (vc - va) + u.z * (ve - va)
                    + u.x * u.y * (va - vb - vc + vd)
                    + u.y * u.z * (va - vc - ve + vg)
                    + u.z * u.x * (va - vb - ve + vf)
                    + (-va + vb + vc - vd + ve - vf - vg + vh) * u.x * u.y * u.z;

                float3 deriv = ga + u.x * (gb - ga) + u.y * (gc - ga) + u.z * (ge - ga)
                    + u.x * u.y * (ga - gb - gc + gd)
                    + u.y * u.z * (ga - gc - ge + gg)
                    + u.z * u.x * (ga - gb - ge + gf)
                    + (-ga + gb + gc - gd + ge - gf - gg + gh) * u.x * u.y * u.z
                    + du * (float3(vb, vc, ve) - va
                        + u.yzx * float3(va - vb - vc + vd, va - vc - ve + vg, va - vb - ve + vf)
                        + u.zxy * float3(va - vb - ve + vf, va - vb - vc + vd, va - vc - ve + vg)
                        + u.yzx * u.zxy * (-va + vb + vc - vd + ve - vf - vg + vh));

                return float4(value, deriv);
            }

            float FootprintMask(float c)
            {
                return pow(smoothstep(_FootprintOuter, _FootprintInner, c), 2.0);
            }

            float Map(float3 p)
            {
                float3 q = p - _FieldCenter.xyz;
                float c = max(0.0, distance(q.xz, _FootprintCenter.xy));
                float cc = FootprintMask(c);
                float4 n = Noised(float3(q.xz * _PrimaryNoiseScale, _Time.y * _PrimaryNoiseSpeed));
                float nn = n.x * length(n.yzw);
                n = Noised(float3(q.xz * _SecondaryNoiseScale, _Time.y * _SecondaryNoiseSpeed));
                nn += 0.25 * n.x * length(n.yzw);
                nn = smoothstep(-0.5, 0.5, nn);
                return q.y - _HeightAmplitude * nn * cc;
            }

            float OriginalMap(float3 p)
            {
                float d = p.y;
                float c = max(0.0, distance(p.xz, _FootprintCenter.xy));
                float cc = pow(smoothstep(_FootprintOuter, _FootprintInner, c), 2.0);
                float4 n = Noised(float3(p.xz * _PrimaryNoiseScale, _Time.y * _PrimaryNoiseSpeed));
                float nn = n.x * length(n.yzw);
                n = Noised(float3(p.xz * _SecondaryNoiseScale, _Time.y * _SecondaryNoiseSpeed));
                nn += 0.25 * n.x * length(n.yzw);
                nn = smoothstep(-0.5, 0.5, nn);
                return d - _HeightAmplitude * nn * cc;
            }

            float OriginalErr(float dist)
            {
                dist = dist / 100.0;
                return min(0.01, dist * dist);
            }

            float3 OriginalDiscontinuityReduce(float3 origin, float3 direction, float3 position)
            {
                [unroll]
                for (int i = 0; i < 6; i++)
                {
                    if (i >= (int)_PrimaryTraceRefineSteps)
                    {
                        break;
                    }

                    position = position + direction * (OriginalMap(position) - OriginalErr(distance(origin, position)));
                }

                return position;
            }

            float3 OriginalIntersect(float3 ro, float3 rd)
            {
                float3 p = ro + rd;
                float t = 0.0;
                [loop]
                for (int i = 0; i < 192; i++)
                {
                    float d = 0.5 * OriginalMap(p);
                    t += d;
                    p += rd * d;
                    if (i >= (int)_PrimaryTraceSteps || d < _PrimaryTraceHitDistance || t > _PrimaryTraceMaxDistance)
                    {
                        break;
                    }
                }

                return OriginalDiscontinuityReduce(ro, rd, p);
            }

            float3 OriginalNormal(float3 p)
            {
                float e = 0.01;
                float3 n = float3(
                    OriginalMap(p + float3(e, 0.0, 0.0)) - OriginalMap(p - float3(e, 0.0, 0.0)),
                    OriginalMap(p + float3(0.0, e, 0.0)) - OriginalMap(p - float3(0.0, e, 0.0)),
                    OriginalMap(p + float3(0.0, 0.0, e)) - OriginalMap(p - float3(0.0, 0.0, e))
                );

                return normalize(n);
            }

            float VolumeField(float3 p)
            {
                float3 q = p - _FieldCenter.xyz;
                float c = distance(q.xz, _FootprintCenter.xy);
                float cc = FootprintMask(c);
                float radialBound = c - _FootprintOuter;
                float top = Map(p);
                float bottom = -(q.y + _BottomDepth * cc);
                return max(max(top, bottom), radialBound);
            }

            bool IntersectUnitBox(float3 ro, float3 rd, out float tNear, out float tFar)
            {
                float3 invRd = 1.0 / max(abs(rd), 1e-5) * sign(rd);
                float3 t0 = (-0.5 - ro) * invRd;
                float3 t1 = (0.5 - ro) * invRd;
                float3 tMin = min(t0, t1);
                float3 tMax = max(t0, t1);
                tNear = max(max(tMin.x, tMin.y), tMin.z);
                tFar = min(min(tMax.x, tMax.y), tMax.z);
                return tFar >= max(tNear, 0.0);
            }

            bool Raymarch(float3 roOS, float3 rdOS, out float3 hitOS, out float travelOS)
            {
                float tNear;
                float tFar;
                if (!IntersectUnitBox(roOS, rdOS, tNear, tFar))
                {
                    hitOS = 0.0;
                    travelOS = 0.0;
                    return false;
                }

                float t = max(tNear, 0.0);
                float maxStepOS = max(0.005, (tFar - t) / max(_RaymarchSteps, 1.0));
                [loop]
                for (int stepIndex = 0; stepIndex < 160; stepIndex++)
                {
                    if (stepIndex >= (int)_RaymarchSteps || t > tFar)
                    {
                        break;
                    }

                    float3 pOS = roOS + rdOS * t;
                    float dField = VolumeField(pOS * _FieldScale);
                    if (dField < _HitDistance)
                    {
                        hitOS = pOS;
                        travelOS = t;
                        return true;
                    }

                    t += min(max(dField / _FieldScale, 0.0025), maxStepOS * 2.5);
                }

                hitOS = 0.0;
                travelOS = tFar;
                return false;
            }

            float3 FieldNormal(float3 pField)
            {
                float e = 0.02;
                return normalize(float3(
                    VolumeField(pField + float3(e, 0.0, 0.0)) - VolumeField(pField - float3(e, 0.0, 0.0)),
                    VolumeField(pField + float3(0.0, e, 0.0)) - VolumeField(pField - float3(0.0, e, 0.0)),
                    VolumeField(pField + float3(0.0, 0.0, e)) - VolumeField(pField - float3(0.0, 0.0, e))
                ));
            }

            float G1V(float dnv, float k)
            {
                return 1.0 / (dnv * (1.0 - k) + k);
            }

            float GGX(float3 n, float3 v, float3 l, float rough, float f0)
            {
                float alpha = rough * rough;
                float3 h = normalize(v + l);
                float dnl = saturate(dot(n, l));
                float dnv = saturate(dot(n, v));
                float dnh = saturate(dot(n, h));
                float dlh = saturate(dot(l, h));
                float asqr = alpha * alpha;
                float den = dnh * dnh * (asqr - 1.0) + 1.0;
                float d = asqr / (PI * den * den);
                float f = f0 + (1.0 - f0) * pow(1.0 - dlh, 5.0);
                float vis = G1V(dnl, alpha) * G1V(dnv, alpha);
                return dnl * d * f * vis;
            }

            float Subsurface(float3 p, float3 v, float3 n)
            {
                float3 d = refract(v, n, 1.0 / max(_ScatterIor, 1.001));
                if (dot(d, d) < 1e-4)
                {
                    d = -n;
                }

                float3 o = p;
                float a = 0.0;
                [loop]
                for (float t = _ScatterStep; t < _ScatterDistance; t += _ScatterStep)
                {
                    o += t * d;
                    a += VolumeField(o);
                }

                float thickness = max(0.0, -a);
                return _ScatterStrength * pow(_ScatterDistance * 0.5, 3.0) / max(thickness, 1e-3);
            }

            float OriginalSubsurface(float3 p, float3 v, float3 n)
            {
                float3 d = refract(v, n, 1.0 / max(_ScatterIor, 1.001));
                if (dot(d, d) < 1e-4)
                {
                    d = -n;
                }

                float3 o = p;
                float a = 0.0;

                [loop]
                for (float stepDistance = 0.1; stepDistance < _ScatterDistance; stepDistance += _ScatterStep)
                {
                    o += stepDistance * d;
                    a += OriginalMap(o);
                }

                float thickness = max(0.0, -a);
                return _ScatterStrength * pow(_ScatterDistance * 0.5, 3.0) / max(thickness, 1e-4);
            }

            float3 OriginalShade(float3 p, float3 v)
            {
                float3 lp = _LightPositionOS.xyz;
                float3 ld = normalize(p + lp);
                float3 n = OriginalNormal(p);
                float fresnel = pow(max(0.0, 1.0 + dot(n, v)), _FresnelPower);

                float3 ambient = _AmbientTint.rgb;
                float3 albedo = _BaseColor.rgb;
                float3 sky = _SkyTint.rgb * 2.0;

                float lamb = max(0.0, dot(n, ld));
                float spec = GGX(n, v, ld, _SpecularRoughness, fresnel);
                float ss = max(0.0, OriginalSubsurface(p, v, n));

                lamb = lerp(lamb, _ScatterBoost * smoothstep(0.0, 2.0, pow(ss, _ScatterCurve)), _ScatterBlend);
                return (ambient + albedo * lamb + _SpecularMultiplier * spec + fresnel * sky) * 0.5;
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.positionOS = input.positionOS.xyz;
                o.screenPos = ComputeScreenPos(pos.positionCS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.screenPos.xy / i.screenPos.w;
                float lr = 0.5 + 0.5 * cos(5.1 * 0.4 - PI);
                lr = smoothstep(0.13, 1.0, lr);

                float3 c = lerp(float3(0.0, 217.0, 0.0), float3(0.0, 4.4, -190.0), pow(lr, 1.0));
                float3x3 rot = RotateAxis(float3(1.0, 0.0, 0.0), PI / 2.0);
                float3x3 ro2 = RotateAxis(float3(1.0, 0.0, 0.0), -0.008 * PI / 2.0);

                float2 u2 = -1.0 + 2.0 * uv;
                u2.x *= _ScreenParams.x / _ScreenParams.y;

                float3 d = lerp(normalize(mul(float3(u2, 20.0), rot)), normalize(mul(normalize(float3(u2, 20.0)), ro2)),
                                            pow(lr, 1.11));
                d = normalize(d);

                float3 hit = OriginalIntersect(c + 145.0 * d, d);
                float3 originalColor = OriginalShade(hit, d);
                float3 n = frac(sin(float3(
                    dot(float3(uv, 0.001 * _Time.y), float3(127.1, 311.7, 74.7)),
                    dot(float3(uv, 0.001 * _Time.y), float3(269.5, 183.3, 246.1)),
                    dot(float3(uv, 0.001 * _Time.y), float3(113.5, 271.9, 124.6))
                )) * 43758.5453123);

                return float4(max(originalColor * (0.99 + 0.02 * n), 0.0), 1.0);
            }
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

    CustomEditor "JadeVolumeShaderGUI"
}