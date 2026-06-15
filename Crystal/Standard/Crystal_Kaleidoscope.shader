Shader "MaterialFX/Crystal/Kaleidoscope"
{
    Properties
    {
        _STResolution("ST Resolution", Vector) = (512,288,0.001953125,0.003472222)
        _STMouse("ST Mouse", Vector) = (0,0,0,0)
        _STTime("ST Time", Float) = 0
        _STDeltaTime("ST DeltaTime", Float) = 0
        _STFrame("ST Frame", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _STResolution;
                float4 _STMouse;
                float _STTime;
                float _STDeltaTime;
                float _STFrame;
            CBUFFER_END

            #define DTR 0.01745329

            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionHCS:SV_POSITION; float2 uv:TEXCOORD0; float2 fragCoord:TEXCOORD1; };

            struct CrystalState
            {
                float3 cp, cn, cr, ro, rd, ss, oc, cc, gl, vb;
                float4 fc;
                float tt, cd, sd, io, oa, td;
                int es, ec;
            };

            float2 Rot2(float2 p, float a)
            {
                float c = cos(a);
                float s = sin(a);
                return float2(c * p.x + s * p.y, -s * p.x + c * p.y);
            }

            CrystalState InitState()
            {
                CrystalState st;
                st.cp = float3(0.0, 0.0, 0.0);
                st.cn = float3(0.0, 0.0, 0.0);
                st.cr = float3(0.0, 0.0, 0.0);
                st.ro = float3(0.0, 0.0, 0.0);
                st.rd = float3(0.0, 0.0, 0.0);
                st.ss = float3(0.0, 0.0, 0.0);
                st.oc = float3(0.0, 0.0, 0.0);
                st.cc = float3(0.0, 0.0, 0.0);
                st.gl = float3(0.0, 0.0, 0.0);
                st.vb = float3(0.0, 0.0, 0.0);
                st.fc = float4(0.0, 0.0, 0.0, 0.0);
                st.tt = 0.0;
                st.cd = 0.0;
                st.sd = 0.0;
                st.io = 0.0;
                st.oa = 0.0;
                st.td = 0.0;
                st.es = 0;
                st.ec = 0;
                return st;
            }

            float3 SafeNormalize3(float3 v)
            {
                float lenSq = dot(v, v);
                if (lenSq <= 1e-12)
                {
                    return float3(0.0, 0.0, 0.0);
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
                    return float3(0.0, 0.0, 0.0);
                }
                return eta * i - (eta * d + sqrt(k)) * n;
            }

            float3 SanitizeColor(float3 c)
            {
                c = any(isnan(c)) ? 0.0.xxx : c;
                c = any(isinf(c)) ? 0.0.xxx : c;
                return max(c, 0.0);
            }

            float BoxSdf(float3 p, float3 s)
            {
                float3 q = abs(p) - s;
                return min(max(q.x, max(q.y, q.z)), 0.0) + length(max(q, 0.0));
            }

            float SmoothMin(float a, float b, float k)
            {
                float h = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
                return lerp(b, a, h) - k * h * (1.0 - h);
            }

            float3 Lattice(float3 p, int iter, float an)
            {
                [loop] for (int i = 0; i < iter; i++)
                {
                    p.xy = Rot2(p.xy, an * DTR);
                    p.yz = abs(p.yz) - 1.0;
                    p.xz = Rot2(p.xz, -an * DTR);
                }
                return p;
            }

            float Map(float3 p, inout CrystalState st)
            {
                if (_STMouse.z > 0.0)
                {
                    p.yz = Rot2(p.yz, 2.0 * (_STMouse.y / _STResolution.y - 0.5));
                    p.zx = Rot2(p.zx, -7.0 * (_STMouse.x / _STResolution.x - 0.5));
                }

                p.xz = Rot2(p.xz, st.tt * 0.1);
                p.xy = Rot2(p.xy, st.tt * 0.1);
                p = Lattice(p, 9, 45.0 + cos(st.tt * 0.1) * 5.0);

                st.sd = BoxSdf(p, float3(1.0, 1.0, 1.0)) - 0.01;
                st.sd = SmoothMin(st.sd, st.sd, 0.8);
                st.gl += exp(-st.sd * 0.001) * SafeNormalize3(max(p * p, 1e-6)) * 0.003;
                st.sd = abs(st.sd) - 0.001;

                if (st.sd < 0.001)
                {
                    st.oc = 1.0;
                    st.io = 1.2;
                    st.oa = 0.0;
                    st.ss = 0.0;
                    st.vb = float3(0.0, 10.0, 2.8);
                    st.ec = 2;
                }
                return st.sd;
            }

            void Trace(inout CrystalState st)
            {
                st.vb.x = 0.0;
                st.cd = 0.0;
                [loop] for (int i = 0; i < 256; i++)
                {
                    Map(st.ro + st.rd * st.cd, st);
                    st.cd += st.sd;
                    st.td += st.sd;
                    if (st.sd < 0.0001 || st.cd > 128.0) break;
                }
            }

            void Normal(inout CrystalState st)
            {
                float3 kx = st.cp - float3(0.001, 0.0, 0.0);
                float3 ky = st.cp - float3(0.0, 0.001, 0.0);
                float3 kz = st.cp - float3(0.0, 0.0, 0.001);
                float center = Map(st.cp, st);
                float dx = Map(kx, st);
                float dy = Map(ky, st);
                float dz = Map(kz, st);
                st.cn = SafeNormalize3(center - float3(dx, dy, dz));
            }

            void Shade(inout CrystalState st)
            {
                st.cc = float3(0.35, 0.25, 0.45) + length(pow(abs(st.rd + float3(0.0, 0.5, 0.0)), 3.0)) * 0.3 + st.gl;
                float3 l = float3(0.9, 0.7, 0.5);
                if (st.cd > 128.0)
                {
                    st.oa = 1.0;
                    return;
                }

                float df = clamp(length(st.cn * l), 0.0, 1.0);
                float3 fr = pow(1.0 - df, 3.0) * lerp(st.cc, float3(0.4, 0.4, 0.4), 0.5);
                float sp = (1.0 - length(cross(st.cr, st.cn * l))) * 0.2;
                float ao = min(Map(st.cp + st.cn * 0.3, st) - 0.3, 0.3) * 0.4;
                st.cc = lerp(st.oc * (df + fr + st.ss) + fr + sp + ao + st.gl, st.oc, st.vb.x);
            }

            float4 RenderCrystal(float2 fragCoord)
            {
                CrystalState st = InitState();
                st.tt = fmod(_STTime + 25.0, 260.0);
                st.io = 1.2;

                float2 uv = fragCoord / _STResolution.xy;
                uv -= 0.5;
                uv /= float2(_STResolution.y / _STResolution.x, 1.0);

                float an = sin(st.tt * 0.3) * 0.5 + 0.5;
                an = 1.0 - pow(1.0 - pow(an, 5.0), 10.0);
                st.ro = float3(0.0, 0.0, -5.0 - an * 15.0);
                st.rd = SafeNormalize3(float3(uv, 1.0));

                [loop] for (int i = 0; i < 25; i++)
                {
                    Trace(st);
                    st.cp = st.ro + st.rd * st.cd;
                    Normal(st);
                    st.ro = st.cp - st.cn * 0.01;
                    st.cr = RefractGLSL(st.rd, st.cn, (i % 2 == 0) ? (1.0 / st.io) : st.io);

                    if (dot(st.cr, st.cr) <= 1e-12 && st.es <= 0)
                    {
                        st.cr = ReflectGLSL(st.rd, st.cn);
                        st.es = st.ec;
                    }

                    if (max(st.es, 0) % 3 == 0 && st.cd < 128.0)
                    {
                        st.rd = st.cr;
                    }
                    st.es--;

                    if (st.vb.x > 0.0 && i % 2 == 1)
                    {
                        st.oa = pow(clamp(st.cd / st.vb.y, 0.0, 1.0), st.vb.z);
                    }

                    Shade(st);
                    st.fc += float4(st.cc * st.oa, st.oa) * (1.0 - st.fc.a);
                    if (st.fc.a >= 1.0 || st.cd > 128.0) break;
                }

                if (!(st.fc.a > 1e-4))
                {
                    return float4(SanitizeColor(st.cc + st.gl), 1.0);
                }
                return float4(SanitizeColor(st.fc.rgb / st.fc.a), 1.0);
            }

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                o.fragCoord = i.uv * _STResolution.xy;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                return RenderCrystal(i.fragCoord);
            }
            ENDHLSL
        }
    }
}
