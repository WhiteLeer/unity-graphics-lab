Shader "SurfaceLab/Crystal/VolumeObject"
{
    Properties
    {
        [MainColor] _BaseColor("参考基色", Color) = (0.35, 0.25, 0.45, 1)
        _AbsorptionColor("透射颜色", Color) = (0.92, 0.92, 1.0, 1)
        _EdgeTint("参考边缘色", Color) = (0.4, 0.4, 0.4, 1)
        _InteriorTint("参考内部光色", Color) = (0.9, 0.7, 0.5, 1)

        _RefractionIndex("折射率", Range(1.01, 2.0)) = 1.2
        _Dispersion("色散", Range(0.0, 0.08)) = 0.012
        _Roughness("表面粗糙度", Range(0.0, 1.0)) = 0.04
        _ThicknessScale("厚度倍率", Range(0.1, 3.0)) = 1.0
        _AbsorptionStrength("吸收强度", Range(0.0, 4.0)) = 0.22
        _RefractionStrength("折射偏移", Range(0.0, 2.0)) = 1.0

        _FacetSize("晶块尺寸", Range(0.2, 2.0)) = 0.72
        _FacetAngle("晶格角度", Range(0.0, 90.0)) = 45.0
        _LatticeIterations("晶格层数", Range(1.0, 9.0)) = 9.0
        _FacetThickness("晶面厚度", Range(0.0001, 0.05)) = 0.001
        _FacetBlend("晶面折叠混合", Range(0.05, 2.0)) = 0.8
        _InteriorStrength("内部晶面强度", Range(0.0, 4.0)) = 1.1
        _FacetGlowStrength("晶面辉光强度", Range(0.0, 0.02)) = 0.003
        _FacetGlowFalloff("晶面辉光衰减", Range(0.0001, 0.01)) = 0.001
        _ColorVariation("方向颜色变化", Range(0.0, 1.0)) = 0.3
        _FacetColorBlend("晶面颜色混合", Range(0.0, 1.0)) = 0.5
        _FacetSpecularStrength("晶面高光强度", Range(0.0, 1.0)) = 0.2

        _InternalTraceSteps("内部追踪步数", Range(1.0, 8.0)) = 8.0
        _InternalBounceCount("内部交互次数", Range(1.0, 2.0)) = 2.0
        _SceneTransmissionBlend("场景透射混合", Range(0.0, 1.0)) = 0.22
        _FinalRefractionBlend("最终折射混合", Range(0.0, 1.0)) = 0.42
        _ReflectionStrength("环境反射强度", Range(0.0, 1.0)) = 0.35
        _HighlightCompression("高光压缩", Range(0.0, 1.0)) = 0.45
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-100"
            "RenderPipeline" = "UniversalPipeline"
        }

        // The shared volume-thickness renderer draws this pass before the main pass.
        Pass
        {
            Name "CrystalThicknessBackface"
            Tags
            {
                "LightMode" = "CrystalThicknessBackface"
            }
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ThicknessVert
            #pragma fragment ThicknessFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct ThicknessAttributes
            {
                float4 positionOS : POSITION;
            };

            struct ThicknessVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            ThicknessVaryings ThicknessVert(ThicknessAttributes input)
            {
                ThicknessVaryings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                return output;
            }

            float ThicknessFrag(ThicknessVaryings input) : SV_Target
            {
                return -TransformWorldToView(input.positionWS).z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "CrystalVolumeObject"
            Tags
            {
                "LightMode" = "UniversalForward"
            }
            Cull Back
            ZWrite On
            Blend One Zero

            HLSLPROGRAM
            #pragma target 4.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _AbsorptionColor;
                float4 _EdgeTint;
                float4 _InteriorTint;
                float _RefractionIndex;
                float _Dispersion;
                float _Roughness;
                float _ThicknessScale;
                float _AbsorptionStrength;
                float _RefractionStrength;
                float _FacetSize;
                float _FacetAngle;
                float _LatticeIterations;
                float _FacetThickness;
                float _FacetBlend;
                float _InteriorStrength;
                float _FacetGlowStrength;
                float _FacetGlowFalloff;
                float _ColorVariation;
                float _FacetColorBlend;
                float _FacetSpecularStrength;
                float _InternalTraceSteps;
                float _InternalBounceCount;
                float _SceneTransmissionBlend;
                float _FinalRefractionBlend;
                float _ReflectionStrength;
                float _HighlightCompression;
            CBUFFER_END

            float _VolumeThicknessAvailable;

            TEXTURE2D_X_FLOAT(_VolumeBackfaceDepthTexture);
            SAMPLER(sampler_VolumeBackfaceDepthTexture);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 viewDirWS : TEXCOORD3;
            };

            struct CrystalThicknessData
            {
                float viewThickness;
                float opticalThickness;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceViewDir(positionInputs.positionWS);
                return output;
            }

            float3 SafeNormalize(float3 value, float3 fallback)
            {
                float lengthSquared = dot(value, value);
                return lengthSquared > 1e-10 ? value * rsqrt(lengthSquared) : fallback;
            }

            float2 Rotate2D(float2 value, float angle)
            {
                float sine;
                float cosine;
                sincos(angle, sine, cosine);
                return float2(
                    cosine * value.x + sine * value.y,
                    -sine * value.x + cosine * value.y);
            }

            float BoxSdf(float3 position, float3 bounds)
            {
                float3 q = abs(position) - bounds;
                return min(max(q.x, max(q.y, q.z)), 0.0) + length(max(q, 0.0));
            }

            float SmoothMin(float a, float b, float smoothing)
            {
                float blend = saturate(0.5 + 0.5 * (b - a) / smoothing);
                return lerp(b, a, blend) - smoothing * blend * (1.0 - blend);
            }

            // This is the static form of the reference shader's nine-fold lattice.
            float3 CrystalLattice(float3 position)
            {
                float angle = radians(_FacetAngle);
                int latticeIterations = clamp((int)_LatticeIterations, 1, 9);
                [unroll]
                for (int iteration = 0; iteration < 9; iteration++)
                {
                    if (iteration >= latticeIterations)
                    {
                        break;
                    }

                    position.xy = Rotate2D(position.xy, angle);
                    position.yz = abs(position.yz) - 1.0;
                    position.xz = Rotate2D(position.xz, -angle);
                }
                return position;
            }

            float CrystalInternalField(
                float3 positionOS,
                out float3 foldedPosition,
                out float3 glowContribution)
            {
                float facetSize = max(_FacetSize, 0.05);
                foldedPosition = CrystalLattice(positionOS / facetSize);
                float signedDistance = BoxSdf(foldedPosition, float3(1.0, 1.0, 1.0)) - _FacetThickness;
                signedDistance = SmoothMin(signedDistance, signedDistance, _FacetBlend);
                float3 axisEnergy = max(
                    foldedPosition * foldedPosition,
                    float3(1e-6, 1e-6, 1e-6));
                glowContribution = exp(-signedDistance * _FacetGlowFalloff) *
                                   SafeNormalize(axisEnergy, float3(0.57735, 0.57735, 0.57735)) *
                                   _FacetGlowStrength;
                return abs(signedDistance) - _FacetThickness;
            }

            float CrystalInternalField(float3 positionOS, out float3 foldedPosition)
            {
                float3 unusedGlow;
                return CrystalInternalField(positionOS, foldedPosition, unusedGlow);
            }

            float ObjectMinimumScale()
            {
                float scaleX = length(TransformObjectToWorldDir(float3(1.0, 0.0, 0.0), false));
                float scaleY = length(TransformObjectToWorldDir(float3(0.0, 1.0, 0.0), false));
                float scaleZ = length(TransformObjectToWorldDir(float3(0.0, 0.0, 1.0), false));
                return max(min(scaleX, min(scaleY, scaleZ)), 1e-4);
            }

            float3 CrystalFacetNormalWS(float3 positionOS, float3 fallbackNormalWS)
            {
                float epsilon = max(_FacetSize * 0.0025, 0.0005);
                const float3 k0 = float3(1.0, -1.0, -1.0);
                const float3 k1 = float3(-1.0, -1.0, 1.0);
                const float3 k2 = float3(-1.0, 1.0, -1.0);
                const float3 k3 = float3(1.0, 1.0, 1.0);
                float3 unusedFold;
                float field0 = CrystalInternalField(positionOS + k0 * epsilon, unusedFold);
                float field1 = CrystalInternalField(positionOS + k1 * epsilon, unusedFold);
                float field2 = CrystalInternalField(positionOS + k2 * epsilon, unusedFold);
                float field3 = CrystalInternalField(positionOS + k3 * epsilon, unusedFold);
                float3 gradient = k0 * field0 + k1 * field1 + k2 * field2 + k3 * field3;
                float3 normalWS = TransformObjectToWorldNormal(SafeNormalize(gradient, float3(0.0, 1.0, 0.0)));
                return SafeNormalize(normalWS, fallbackNormalWS);
            }

            float TraceCrystalFacet(
                float3 originWS,
                float3 directionWS,
                float maximumDistance,
                out float3 facetPositionWS,
                out float3 facetNormalWS,
                out float3 facetAxisWeights,
                out float3 traceGlow,
                out float traveledDistance)
            {
                float objectScale = ObjectMinimumScale();
                float facetSize = max(_FacetSize, 0.05);
                float hitEpsilon = max(maximumDistance * 0.0015, objectScale * 0.0005);
                float minimumStep = max(maximumDistance / 96.0, hitEpsilon * 0.5);
                float travel = max(hitEpsilon * 2.0, maximumDistance * 0.01);
                float bestDistance = 1e8;
                float3 bestPositionWS = originWS + directionWS * (maximumDistance * 0.5);
                float3 bestFoldedPosition = float3(1.0, 1.0, 1.0);
                float bestTravel = maximumDistance * 0.5;
                traceGlow = 0.0;

                [loop]
                int traceSteps = clamp((int)_InternalTraceSteps, 1, 8);
                for (int stepIndex = 0; stepIndex < 8; stepIndex++)
                {
                    if (stepIndex >= traceSteps)
                    {
                        break;
                    }

                    float3 samplePositionWS = originWS + directionWS * travel;
                    float3 samplePositionOS = TransformWorldToObject(samplePositionWS);
                    float3 foldedPosition;
                    float3 glowContribution;
                    float fieldDistance = CrystalInternalField(
                        samplePositionOS,
                        foldedPosition,
                        glowContribution);
                    traceGlow += glowContribution;
                    float distanceWS = abs(fieldDistance) * facetSize * objectScale;

                    if (distanceWS < bestDistance)
                    {
                        bestDistance = distanceWS;
                        bestPositionWS = samplePositionWS;
                        bestFoldedPosition = foldedPosition;
                        bestTravel = travel;
                    }

                    if (distanceWS <= hitEpsilon || travel >= maximumDistance)
                    {
                        break;
                    }

                    travel += max(distanceWS * 0.65, minimumStep);
                }

                float3 bestPositionOS = TransformWorldToObject(bestPositionWS);
                facetPositionWS = bestPositionWS;
                facetNormalWS = CrystalFacetNormalWS(bestPositionOS, -directionWS);
                float3 axisEnergy = max(bestFoldedPosition * bestFoldedPosition, float3(1e-5, 1e-5, 1e-5));
                facetAxisWeights = axisEnergy / max(dot(axisEnergy, float3(1.0, 1.0, 1.0)), 1e-5);
                traveledDistance = bestTravel;

                float visibilityWidth = max(maximumDistance * 0.045, objectScale * 0.0025);
                return exp(-bestDistance / visibilityWidth);
            }

            CrystalThicknessData ResolveThickness(Varyings input, float3 normalWS, float3 viewDirWS, float3 refractedWS)
            {
                CrystalThicknessData data;
                float2 screenUV = UnityStereoTransformScreenSpaceTex(GetNormalizedScreenSpaceUV(input.positionCS));
                float backfaceDepth = SAMPLE_TEXTURE2D_X(
                    _VolumeBackfaceDepthTexture,
                    sampler_VolumeBackfaceDepthTexture,
                    screenUV).r;
                float frontfaceDepth = -TransformWorldToView(input.positionWS).z;
                float measuredThickness = max(backfaceDepth - frontfaceDepth, 0.0);
                float validMeasurement = saturate(_VolumeThicknessAvailable) * step(1e-4, measuredThickness);
                float fallbackThickness = ObjectMinimumScale();
                data.viewThickness = lerp(fallbackThickness, measuredThickness, validMeasurement) * _ThicknessScale;

                float normalThickness = data.viewThickness * max(dot(normalWS, viewDirWS), 0.02);
                data.opticalThickness = normalThickness / max(abs(dot(refractedWS, normalWS)), 0.04);
                data.opticalThickness = min(data.opticalThickness, data.viewThickness * 4.0);
                return data;
            }

            float2 RefractedScreenOffset(float3 refractedWS, float opticalThickness, float frontfaceDepth)
            {
                float3 refractedVS = TransformWorldToViewDir(refractedWS, false);
                float2 projectedDirection = refractedVS.xy / max(abs(refractedVS.z), 0.08);
                return projectedDirection * (opticalThickness / max(frontfaceDepth, 0.1)) *
                       (0.5 * _RefractionStrength);
            }

            float3 SampleDispersedTransmission(
                Varyings input,
                float3 incidentWS,
                float3 normalWS,
                float opticalThickness,
                out float3 centerRefractedWS)
            {
                float ior = max(_RefractionIndex, 1.001);
                float dispersion = min(_Dispersion, (ior - 1.001) * 0.45);
                float3 refractedR = SafeNormalize(refract(incidentWS, normalWS, rcp(ior - dispersion)), -normalWS);
                float3 refractedG = SafeNormalize(refract(incidentWS, normalWS, rcp(ior)), -normalWS);
                float3 refractedB = SafeNormalize(refract(incidentWS, normalWS, rcp(ior + dispersion)), -normalWS);
                centerRefractedWS = refractedG;

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float frontfaceDepth = -TransformWorldToView(input.positionWS).z;
                float2 uvR = saturate(screenUV + RefractedScreenOffset(refractedR, opticalThickness, frontfaceDepth));
                float2 uvG = saturate(screenUV + RefractedScreenOffset(refractedG, opticalThickness, frontfaceDepth));
                float2 uvB = saturate(screenUV + RefractedScreenOffset(refractedB, opticalThickness, frontfaceDepth));
                float3 sceneR = SampleSceneColor(uvR);
                float3 sceneG = SampleSceneColor(uvG);
                float3 sceneB = SampleSceneColor(uvB);
                float3 sceneTransmission = float3(sceneR.r, sceneG.g, sceneB.b);

                float3 environmentG = GlossyEnvironmentReflection(refractedG, input.positionWS, _Roughness, 1.0);
                float3 environmentTransmission = environmentG;
                float sceneSampleValid = step(
                    1e-5,
                    dot(abs(sceneR) + abs(sceneG) + abs(sceneB), float3(1.0, 1.0, 1.0)));
                return lerp(
                    environmentTransmission,
                    lerp(environmentTransmission, sceneTransmission, _SceneTransmissionBlend),
                    sceneSampleValid);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = SafeNormalize(input.normalWS, float3(0.0, 1.0, 0.0));
                float3 viewDirWS = SafeNormalize(input.viewDirWS, normalWS);
                float3 incidentWS = -viewDirWS;
                float ior = max(_RefractionIndex, 1.001);
                float3 centerRefractedWS = SafeNormalize(refract(incidentWS, normalWS, rcp(ior)), -normalWS);
                CrystalThicknessData thickness = ResolveThickness(input, normalWS, viewDirWS, centerRefractedWS);

                float3 transmission = SampleDispersedTransmission(
                    input,
                    incidentWS,
                    normalWS,
                    thickness.opticalThickness,
                    centerRefractedWS);
                float3 absorptionCoefficient = -log(max(_AbsorptionColor.rgb, float3(0.02, 0.02, 0.02))) *
                                               _AbsorptionStrength;
                float3 transmittance = exp(-absorptionCoefficient * thickness.opticalThickness);
                transmission *= transmittance;

                float f0Base = (ior - 1.0) / (ior + 1.0);
                float f0 = f0Base * f0Base;
                float ndv = saturate(dot(normalWS, viewDirWS));
                float surfaceFresnel = f0 + (1.0 - f0) * pow(1.0 - ndv, 5.0);
                float3 reflectionDirection = reflect(incidentWS, normalWS);
                float3 reflection = GlossyEnvironmentReflection(
                    reflectionDirection,
                    input.positionWS,
                    _Roughness,
                    1.0);

                float objectScale = ObjectMinimumScale();
                float rayOffset = max(objectScale * 0.001, thickness.opticalThickness * 0.001);
                float3 currentOriginWS = input.positionWS + centerRefractedWS * rayOffset;
                float3 currentDirectionWS = centerRefractedWS;
                float remainingDistance = max(thickness.opticalThickness - rayOffset, rayOffset);
                float3 accumulatedGlow = 0.0;
                float3 accumulatedFacets = 0.0;
                float accumulatedWeight = 0.0;
                float3 referenceLight = _InteriorTint.rgb;

                [loop]
                int bounceCount = clamp((int)_InternalBounceCount, 1, 2);
                for (int bounceIndex = 0; bounceIndex < 2; bounceIndex++)
                {
                    if (bounceIndex >= bounceCount)
                    {
                        break;
                    }

                    float3 facetPositionWS;
                    float3 facetNormalWS;
                    float3 facetAxisWeights;
                    float3 traceGlow;
                    float traveledDistance;
                    float facetVisibility = TraceCrystalFacet(
                        currentOriginWS,
                        currentDirectionWS,
                        remainingDistance,
                        facetPositionWS,
                        facetNormalWS,
                        facetAxisWeights,
                        traceGlow,
                        traveledDistance);
                    accumulatedGlow += traceGlow;

                    if (facetVisibility <= 0.03)
                    {
                        break;
                    }

                    if (dot(currentDirectionWS, facetNormalWS) > 0.0)
                    {
                        facetNormalWS = -facetNormalWS;
                    }

                    float directionEnergy = length(
                        pow(abs(currentDirectionWS + float3(0.0, 0.5, 0.0)), 3.0));
                    float3 referenceColor = _BaseColor.rgb + directionEnergy * _ColorVariation +
                                            accumulatedGlow * _InteriorStrength;
                    float diffuseReference = saturate(length(facetNormalWS * referenceLight));
                    float fresnelReference = pow(1.0 - diffuseReference, 3.0);
                    float3 edgeReference = fresnelReference *
                                           lerp(referenceColor, _EdgeTint.rgb, _FacetColorBlend);
                    float referenceSpecular = max(
                        1.0 - length(cross(currentDirectionWS, facetNormalWS * referenceLight)),
                        0.0) * _FacetSpecularStrength;
                    float3 axisTint = lerp(_InteriorTint.rgb, _EdgeTint.rgb, facetAxisWeights);
                    float3 facetColor = referenceColor * diffuseReference +
                                        edgeReference +
                                        referenceSpecular +
                                        accumulatedGlow * axisTint * _InteriorStrength;
                    float bounceWeight = facetVisibility / (1.0 + bounceIndex * 0.65);
                    accumulatedFacets += facetColor * bounceWeight;
                    accumulatedWeight += bounceWeight;

                    float eta = (bounceIndex % 2 == 0) ? ior : rcp(ior);
                    float3 nextDirectionWS = refract(currentDirectionWS, facetNormalWS, eta);
                    if (dot(nextDirectionWS, nextDirectionWS) <= 1e-8)
                    {
                        nextDirectionWS = reflect(currentDirectionWS, facetNormalWS);
                    }
                    currentDirectionWS = SafeNormalize(nextDirectionWS, currentDirectionWS);
                    currentOriginWS = facetPositionWS + currentDirectionWS * rayOffset;
                    remainingDistance -= traveledDistance + rayOffset;
                    if (remainingDistance <= rayOffset)
                    {
                        break;
                    }
                }

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float frontfaceDepth = -TransformWorldToView(input.positionWS).z;
                float2 finalRefractionUV = saturate(
                    screenUV + RefractedScreenOffset(
                        currentDirectionWS,
                        thickness.opticalThickness,
                        frontfaceDepth));
                float3 finalSceneTransmission = SampleSceneColor(finalRefractionUV);
                float finalSceneValid = step(
                    1e-5,
                    dot(abs(finalSceneTransmission), float3(1.0, 1.0, 1.0)));
                transmission = lerp(
                    transmission,
                    finalSceneTransmission,
                    finalSceneValid * _FinalRefractionBlend);

                float finalDirectionEnergy = length(
                    pow(abs(currentDirectionWS + float3(0.0, 0.5, 0.0)), 3.0));
                float3 referenceFinal = _BaseColor.rgb + finalDirectionEnergy * _ColorVariation +
                                        accumulatedGlow * _InteriorStrength;
                float3 facetAverage = accumulatedFacets / max(accumulatedWeight, 1e-4);
                float facetBlend = saturate(accumulatedWeight * _FacetColorBlend);
                float3 referenceCrystal = lerp(referenceFinal, facetAverage, facetBlend);

                float3 edgeColor = _EdgeTint.rgb * pow(1.0 - ndv, 3.0);
                float reflectionWeight = surfaceFresnel * _ReflectionStrength;
                float3 color = lerp(referenceCrystal, transmission, _SceneTransmissionBlend) *
                               (1.0 - reflectionWeight) +
                               reflection * reflectionWeight +
                               edgeColor;

                // Preserve HDR highlights for Bloom while preventing parameter-driven blowout.
                float peak = max(color.r, max(color.g, color.b));
                color /= 1.0 + max(peak - 1.0, 0.0) * _HighlightCompression;
                return float4(max(color, 0.0), 1.0);
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
    }
}
