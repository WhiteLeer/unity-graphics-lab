using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_RENDER_PIPELINE_UNIVERSAL || UNITY_2021_3_OR_NEWER
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public sealed class CrystalKaleidoscopeBootstrap : MonoBehaviour
{
    [SerializeField] private Material sourceMaterial;
    [SerializeField] private string quadObjectName = "Crystal_Quad";

    private Material runtimeMaterial;
    private Camera runtimeCamera;
    private Transform runtimeQuadTransform;
    private int frameIndex;
    private bool rendererFeaturesSuppressed;

#if UNITY_EDITOR && (UNITY_RENDER_PIPELINE_UNIVERSAL || UNITY_2021_3_OR_NEWER)
    private static readonly string[] InterferingRendererFeatureNames = { "SSRRenderFeature" };
    private static readonly Dictionary<ScriptableRendererFeature, bool> PreviousRendererFeatureStates = new();
#endif

    private void OnEnable()
    {
        frameIndex = 0;
        SuppressInterferingRendererFeatures();
        EnsureSceneSetup();
        PushCommonUniforms(runtimeMaterial);
        FitQuadToCamera();
    }

    private void Update()
    {
        EnsureSceneSetup();
        PushCommonUniforms(runtimeMaterial);
        FitQuadToCamera();
        frameIndex++;
    }

    private void OnDisable()
    {
        RestoreSuppressedRendererFeatures();

        if (runtimeQuadTransform != null)
        {
            var renderer = runtimeQuadTransform.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = sourceMaterial;
            }
        }

        if (runtimeMaterial == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(runtimeMaterial);
        }
        else
        {
            DestroyImmediate(runtimeMaterial);
        }

        runtimeMaterial = null;
        runtimeQuadTransform = null;
    }

    private void EnsureSceneSetup()
    {
        var cam = GetComponent<Camera>();
        if (cam == null)
        {
            cam = Camera.main;
        }

        if (cam == null)
        {
            return;
        }

        runtimeCamera = cam;

        if (runtimeMaterial == null || !Application.isPlaying)
        {
            var shader = Shader.Find("MaterialFX/Crystal/Kaleidoscope");
            if (shader == null)
            {
                Debug.LogError("Shader not found: MaterialFX/Crystal/Kaleidoscope");
                return;
            }

            if (runtimeMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(runtimeMaterial);
                }
                else
                {
                    DestroyImmediate(runtimeMaterial);
                }
            }

            runtimeMaterial = sourceMaterial != null ? new Material(sourceMaterial) : new Material(shader);
            runtimeMaterial.shader = shader;
            runtimeMaterial.name = "M_Crystal_Kaleidoscope_Runtime";
        }

        var quad = GameObject.Find(quadObjectName);
        if (quad == null)
        {
            quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = quadObjectName;
            quad.transform.position = Vector3.zero;
            quad.transform.rotation = Quaternion.identity;
            quad.transform.localScale = Vector3.one;

            var colliderComponent = quad.GetComponent<Collider>();
            if (colliderComponent != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(colliderComponent);
                }
                else
                {
                    DestroyImmediate(colliderComponent);
                }
            }
        }

        runtimeQuadTransform = quad.transform;

        var renderer = quad.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = runtimeMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }
    }

    private void PushCommonUniforms(Material material)
    {
        if (material == null || runtimeCamera == null)
        {
            return;
        }

        var w = Mathf.Max(1, runtimeCamera.pixelWidth);
        var h = Mathf.Max(1, runtimeCamera.pixelHeight);
        material.SetVector("_STResolution", new Vector4(w, h, 1f / w, 1f / h));
        material.SetFloat("_STTime", Time.realtimeSinceStartup);
        material.SetFloat("_STDeltaTime", Application.isPlaying ? Time.deltaTime : (1f / 60f));
        material.SetFloat("_STFrame", frameIndex);

        var mousePos = Input.mousePosition;
        var mouseDown = Input.GetMouseButton(0) ? 1f : 0f;
        material.SetVector("_STMouse", new Vector4(mousePos.x, mousePos.y, mouseDown, mouseDown));
    }

    private void FitQuadToCamera()
    {
        if (runtimeCamera == null || runtimeQuadTransform == null || !runtimeCamera.orthographic)
        {
            return;
        }

        var h = runtimeCamera.orthographicSize * 2f;
        var w = h * runtimeCamera.aspect;
        runtimeQuadTransform.position = Vector3.zero;
        runtimeQuadTransform.rotation = Quaternion.identity;
        runtimeQuadTransform.localScale = new Vector3(w, h, 1f);
    }

    private void SuppressInterferingRendererFeatures()
    {
#if UNITY_EDITOR && (UNITY_RENDER_PIPELINE_UNIVERSAL || UNITY_2021_3_OR_NEWER)
        if (rendererFeaturesSuppressed)
        {
            return;
        }

        var pipelineAsset = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
        if (pipelineAsset == null)
        {
            pipelineAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        }

        if (pipelineAsset == null)
        {
            return;
        }

        var serializedPipeline = new SerializedObject(pipelineAsset);
        var rendererList = serializedPipeline.FindProperty("m_RendererDataList");
        if (rendererList == null || !rendererList.isArray)
        {
            return;
        }

        for (int i = 0; i < rendererList.arraySize; i++)
        {
            var rendererData = rendererList.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererData;
            if (rendererData == null)
            {
                continue;
            }

            var rendererDirty = false;
            foreach (var feature in rendererData.rendererFeatures)
            {
                if (feature == null)
                {
                    continue;
                }

                var shouldSuppress = Array.Exists(
                    InterferingRendererFeatureNames,
                    featureName => string.Equals(feature.name, featureName, StringComparison.Ordinal)
                );

                if (!shouldSuppress)
                {
                    continue;
                }

                if (!PreviousRendererFeatureStates.ContainsKey(feature))
                {
                    PreviousRendererFeatureStates[feature] = feature.isActive;
                }

                if (feature.isActive)
                {
                    feature.SetActive(false);
                    EditorUtility.SetDirty(feature);
                    rendererDirty = true;
                }
            }

            if (rendererDirty)
            {
                rendererData.SetDirty();
                EditorUtility.SetDirty(rendererData);
            }
        }

        rendererFeaturesSuppressed = true;
#endif
    }

    private void RestoreSuppressedRendererFeatures()
    {
#if UNITY_EDITOR && (UNITY_RENDER_PIPELINE_UNIVERSAL || UNITY_2021_3_OR_NEWER)
        if (!rendererFeaturesSuppressed)
        {
            return;
        }

        foreach (var kvp in PreviousRendererFeatureStates)
        {
            if (kvp.Key == null)
            {
                continue;
            }

            kvp.Key.SetActive(kvp.Value);
            EditorUtility.SetDirty(kvp.Key);
        }

        PreviousRendererFeatureStates.Clear();
        rendererFeaturesSuppressed = false;
#endif
    }
}
