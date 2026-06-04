Shader "SurfaceLab/JadeVolume/VolumeObjectSimpleJade"
{
    Properties
    {
        [MainColor] _BaseColor("主体颜色", Color) = (0.72, 0.86, 0.4, 1)
        _AmbientTint("环境底色", Color) = (0.08, 0.18, 0.04, 1)
        _ScatterColor("透射颜色", Color) = (0.78, 0.96, 0.78, 1)
        _SkyTint("边缘冷光", Color) = (0.55, 0.78, 0.82, 1)
        _DensityTex("三维噪声图", 3D) = "" {}

        _NoiseFrequency("噪声频率", Range(0.25, 8.0)) = 2.2
        _NoiseAmount("噪声扰动", Range(0.0, 0.25)) = 0.08
        _ShapeMode("形状模式", Range(0, 3)) = 0
        _ShapeBlend("形体圆润", Range(0.0, 0.2)) = 0.04
        _SurfaceOffset("表面偏移", Range(-0.1, 0.1)) = 0.0

        _ThicknessSampleCount("厚度采样次数", Range(4, 64)) = 32
        _ThicknessSampleDepth("厚度采样深度", Range(0.05, 2.0)) = 1.0
        _SSSAmbient("基础透光", Range(0.01, 1.0)) = 0.18
        _SSSDistortion("透光偏折", Range(0.01, 2.0)) = 0.55
        _SSSPower("透光集中", Range(0.01, 2.0)) = 0.75
        _SSSScale("透光强度", Range(0.01, 5.0)) = 1.45

        _FresnelPower("边缘亮度范围", Range(0.2, 8.0)) = 3.5
        _SpecularRoughness("高光柔和度", Range(0.02, 1.0)) = 0.28
        _SpecularMultiplier("高光强度", Range(0.0, 8.0)) = 1.6

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
            Name "JadeVolumeObject"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _AmbientTint;
                float4 _ScatterColor;
                float4 _SkyTint;
                float _NoiseFrequency;
                float _NoiseAmount;
                float _ShapeMode;
                float _ShapeBlend;
                float _SurfaceOffset;
                float _ThicknessSampleCount;
                float _ThicknessSampleDepth;
                float _SSSAmbient;
                float _SSSDistortion;
                float _SSSPower;
                float _SSSScale;
                float _FresnelPower;
                float _SpecularRoughness;
                float _SpecularMultiplier;
                float _TraceSteps;
                float _HitEpsilon;
                float _MaxDistance;
                float _NormalStep;
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
                float3 positionWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.viewDirWS = GetWorldSpaceViewDir(pos.positionWS);
                return o;
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

            float SampleNoise(float3 positionOS)
            {
                float3 uvw = saturate(positionOS * _NoiseFrequency + 0.5);
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

            float Field(float3 p)
            {
                float noise = SampleNoise(p) - 0.5;
                return BaseShapeSdf(p) - noise * _NoiseAmount - _SurfaceOffset;
            }

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

                    t += max(abs(d) * 0.65, _HitEpsilon * 0.5);
                }

                hitOS = 0.0;
                hitWS = 0.0;
                return false;
            }

            float3 VolumeNormal(float3 p)
            {
                float e = _NormalStep;
                float3 n = float3(
                    Field(p + float3(e, 0.0, 0.0)) - Field(p - float3(e, 0.0, 0.0)),
                    Field(p + float3(0.0, e, 0.0)) - Field(p - float3(0.0, e, 0.0)),
                    Field(p + float3(0.0, 0.0, e)) - Field(p - float3(0.0, 0.0, e))
                );
                return normalize(n);
            }

            float G1V(float dnv, float k)
            {
                return 1.0 / (dnv * (1.0 - k) + k);
            }

            float Hash11(float p)
            {
                float3 p3 = frac(float3(p, p, p) * 0.1031);
                p3 += dot(p3, p3.yzx + 19.19);
                return frac((p3.x + p3.y) * p3.z);
            }

            float3 Hash33(float3 p3)
            {
                p3 = frac(p3 * float3(443.897, 441.423, 437.195));
                p3 += dot(p3, p3.yxz + 19.19);
                return frac((p3.xxy + p3.yxx) * p3.zyx);
            }

            float3 MirrorVector(float3 v, float3 n)
            {
                return v + 2.0 * n * max(0.0, -dot(n, v));
            }

            float3 GenerateThicknessSampleVector(float3 pos, float3 norm, float i)
            {
                float3 randDir = normalize(Hash33(norm + pos * 0.37 + i));
                return MirrorVector(randDir, norm);
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

            float CalculateLocalThickness(float3 p, float3 n)
            {
                float thickness = 0.0;
                int sampleCount = max(1, (int)round(_ThicknessSampleCount));

                [loop]
                for (int i = 0; i < 64; i++)
                {
                    if (i >= sampleCount)
                    {
                        break;
                    }

                    float fi = (float)i;
                    float sampleLength = Hash11(fi + dot(p, float3(12.9898, 78.233, 37.719))) * _ThicknessSampleDepth;
                    float3 sampleDir = GenerateThicknessSampleVector(p, -n, fi);
                    thickness += sampleLength + Field(p + sampleDir * sampleLength);
                }

                return saturate(thickness / max((float)sampleCount, 1.0));
            }

            float VolumeSubsurface(float3 p, float3 v, float3 n, float3 hitWS)
            {
                float3 toLightWS = _VolumeLightPositionWS - hitWS;
                float attenuation = _VolumeLightIntensity / (1.0 + length(toLightWS) + dot(toLightWS, toLightWS));
                float thickness = CalculateLocalThickness(p, n);
                float3 toEye = -v;
                float3 lightDirOS = normalize(TransformWorldToObjectDir(toLightWS));
                float3 sssLight = lightDirOS + n * _SSSDistortion;
                float sssDot = pow(saturate(dot(toEye, -sssLight)), _SSSPower) * _SSSScale;
                return (sssDot + _SSSAmbient) * thickness * attenuation;
            }

            float3 ShadeVolume(float3 p, float3 v, float3 hitWS)
            {
                float3 lightDirWS = normalize(_VolumeLightPositionWS - hitWS);
                float3 lightDirOS = normalize(TransformWorldToObjectDir(lightDirWS));
                float3 n = VolumeNormal(p);
                float fresnel = pow(max(0.0, 1.0 + dot(n, v)), _FresnelPower);

                float3 ambient = _AmbientTint.rgb;
                float3 albedo = _BaseColor.rgb;
                float3 sky = _SkyTint.rgb * 2.0;

                float lamb = max(0.0, dot(n, lightDirOS));
                float spec = GGX(n, v, lightDirOS, _SpecularRoughness, fresnel);
                float ss = max(0.0, VolumeSubsurface(p, v, n, hitWS));

                float lightDistance = max(distance(_VolumeLightPositionWS, hitWS), 1e-3);
                float attenuation = _VolumeLightIntensity / (1.0 + lightDistance * lightDistance);
                float3 lit = ambient * 0.22;
                lit += (ambient + albedo * lamb + _SpecularMultiplier * spec + fresnel * sky) * (_VolumeLightColor.rgb * attenuation * 0.5);
                lit += _ScatterColor.rgb * (_VolumeLightColor.rgb * ss);
                return lit;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 viewDirWS = normalize(i.viewDirWS);
                float3 rayOriginOS = TransformWorldToObject(GetCameraPositionWS());
                float3 rayDirOS = normalize(TransformWorldToObjectDir(-viewDirWS));

                float3 hitOS;
                float3 hitWS;
                if (!TraceVolume(rayOriginOS, rayDirOS, hitOS, hitWS))
                {
                    discard;
                }

                float3 color = ShadeVolume(hitOS, rayDirOS, hitWS);
                return float4(saturate(color), 1.0);
            }
            ENDHLSL
        }
    }
}
