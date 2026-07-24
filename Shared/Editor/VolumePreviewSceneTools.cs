using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public static class VolumePreviewSceneTools
{
    private const string RootPath = "Assets/unity-graphics-lab";
    private const string SharedPrefabPath = "Assets/unity-graphics-lab/Shared/Prefabs/PreviewCarrierTemplate.prefab";
    private const string CalibrationPrefabPath = "Assets/unity-graphics-lab/Shared/Prefabs/LookDevCalibration.prefab";
    private const string CalibrationVolumeProfilePath = "Assets/unity-graphics-lab/Shared/Pipeline/LookDev/LookDevCalibrationVolumeProfile.asset";
    private const string CalibrationBallMaterialPath = "Assets/unity-graphics-lab/Shared/Materials/M_LookDevCalibrationBall.mat";
    private const string CalibrationChartTexturePath = "Assets/unity-graphics-lab/Shared/Textures/T_LookDevColorChart.png";
    private const string CalibrationChartMaterialPath = "Assets/unity-graphics-lab/Shared/Materials/M_LookDevColorChart.mat";
    private const string LookDevScenePath = "Assets/unity-graphics-lab/Shared/Scenes/LookDev.unity";
    private const string CrystalScenePath = "Assets/unity-graphics-lab/Crystal/Scenes/Graphics-Crystal-Example.unity";
    private const string JadeScenePath = "Assets/unity-graphics-lab/JadeVolume/Scenes/Graphics-JadeVolume-Example.unity";
    private const string WaterScenePath = "Assets/unity-graphics-lab/Water/Scenes/Graphics-Water-Example.unity";

    private static readonly Dictionary<string, string> ProfileByScene = new Dictionary<string, string>
    {
        { CrystalScenePath, "Assets/unity-graphics-lab/Shared/Profiles/CrystalVolumePreviewSceneProfile.asset" },
        { JadeScenePath, "Assets/unity-graphics-lab/Shared/Profiles/JadeVolumePreviewSceneProfile.asset" },
        { WaterScenePath, "Assets/unity-graphics-lab/Shared/Profiles/WaterPreviewSceneProfile.asset" }
    };

    [MenuItem("Tools/Unity Graphics Lab/预览系统/迁移所有已配置场景")]
    [MenuItem("Tools/Unity Graphics Lab/Preview/Migrate Configured Scenes")]
    public static void MigrateKnownScenes()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        var originalSetup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            EnsureCalibrationAssets();

            var scenePaths = new HashSet<string>(ProfileByScene.Keys) { LookDevScenePath };
            foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { RootPath }))
            {
                scenePaths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            foreach (var scenePath in scenePaths)
            {
                if (ProfileByScene.TryGetValue(scenePath, out var profilePath))
                {
                    MigrateScene(scenePath, profilePath);
                    EnsureSceneCalibration(scenePath);
                }
                else
                {
                    EnsureSceneCalibration(scenePath);
                }
            }
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Unity Graphics Lab: 预览场景迁移完成。");
    }

    [MenuItem("Tools/Unity Graphics Lab/Preview/Ensure LookDev Calibration")]
    public static void EnsureLookDevCalibration()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        var originalSetup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            EnsureCalibrationAssets();
            foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { RootPath }))
            {
                EnsureSceneCalibration(AssetDatabase.GUIDToAssetPath(guid));
            }
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Unity Graphics Lab: LookDev 校准配置已应用到所有示例场景。");
    }

    [MenuItem("Tools/Unity Graphics Lab/预览系统/校验所有已配置场景")]
    [MenuItem("Tools/Unity Graphics Lab/Preview/Validate Configured Scenes")]
    public static void ValidateKnownScenes()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        var originalSetup = EditorSceneManager.GetSceneManagerSetup();
        var errorCount = 0;
        try
        {
            foreach (var scenePath in ProfileByScene.Keys)
            {
                errorCount += ValidateScene(scenePath, ProfileByScene[scenePath]);
            }
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
        }

        if (errorCount == 0)
        {
            Debug.Log("Unity Graphics Lab: 预览场景校验通过。");
        }
        else
        {
            Debug.LogError($"Unity Graphics Lab: 预览场景校验发现 {errorCount} 个问题。");
        }
    }

    private static void MigrateScene(string scenePath, string profilePath)
    {
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var profile = AssetDatabase.LoadAssetAtPath<VolumePreviewSceneProfile>(profilePath);
        if (profile == null)
        {
            Debug.LogError($"找不到场景配置：{profilePath}");
            return;
        }

        var root = FindSceneObject("LookDevRoot") ?? FindSceneObject("SceneTemplate");
        if (root == null)
        {
            Debug.LogError($"场景缺少 LookDevRoot/SceneTemplate：{scenePath}");
            return;
        }

        root.name = "LookDevRoot";

        var camera = FindSceneObject("Main Camera")?.GetComponent<Camera>();
        var toggle = root.GetComponent<ReferencePreviewToggleController>();
        var carrierRoot = FindSceneObject("PreviewCarrierTemplate");
        var preview = carrierRoot != null ? carrierRoot.GetComponent<VolumeMaterialPreviewController>() : null;
        var interaction = carrierRoot != null ? carrierRoot.GetComponent<PreviewInteractionController>() : null;
        var render = carrierRoot != null ? carrierRoot.GetComponent<VolumePreviewRenderController>() : null;

        if (toggle == null || preview == null || interaction == null || render == null || camera == null)
        {
            Debug.LogError($"场景公共控制器或相机不完整：{scenePath}");
            return;
        }

        if (carrierRoot == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SharedPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"找不到共享载体预制体：{SharedPrefabPath}");
                return;
            }

            carrierRoot = PrefabUtility.InstantiatePrefab(prefab, root.transform) as GameObject;
            if (carrierRoot == null)
            {
                Debug.LogError($"无法实例化共享载体预制体：{scenePath}");
                return;
            }
        }

        var quad = FindChild(carrierRoot.transform, "Preview");
        var sphere = FindChild(carrierRoot.transform, "PreviewCarrier_Sphere");
        var cube = FindChild(carrierRoot.transform, "PreviewCarrier_Cube");
        var capsule = FindChild(carrierRoot.transform, "PreviewCarrier_Capsule");
        var plane = FindChild(carrierRoot.transform, "Plane");
        var matBall = FindChild(carrierRoot.transform, "PreviewCarrier_MatBall");
        var pointLight = FindSceneObject("Preview_PointLight")?.GetComponent<Light>();

        SetObjectReference(toggle, "previewController", preview);
        SetObjectReference(toggle, "templateProfile", profile);
        SetObjectReference(toggle, "targetCamera", camera);

        SetObjectReference(preview, "templateProfile", profile);
        SetObjectReference(preview, "interactionController", interaction);
        SetObjectReference(preview, "renderController", render);

        SetObjectReference(interaction, "previewCamera", camera);
        SetObjectReference(render, "sourceLight", pointLight);
        SetObjectReference(render, "legacyQuadCarrierObject", quad);
        SetObjectReference(render, "sphereCarrierObject", sphere);
        SetObjectReference(render, "cubeCarrierObject", cube);
        SetObjectReference(render, "capsuleCarrierObject", capsule);
        SetObjectArrayReferences(render, "modeCarrierObjects", ResolveModeCarriers(profile, plane, matBall));

        profile.SceneCameraDefaults.ApplyTo(camera);
        camera.backgroundColor = profile.CameraBackgroundColor;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void EnsureSceneCalibration(string scenePath)
    {
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var camera = FindSceneObject("Main Camera")?.GetComponent<Camera>();
        if (camera == null)
        {
            return;
        }

        EnsureCalibrationAssets();
        var calibrationPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CalibrationPrefabPath);
        var volumeProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(CalibrationVolumeProfilePath);
        if (calibrationPrefab == null || volumeProfile == null)
        {
            Debug.LogError($"LookDev 校准资源不完整：{scenePath}");
            return;
        }

        var calibration = FindSceneObject("LookDevCalibration");
        if (calibration == null)
        {
            calibration = PrefabUtility.InstantiatePrefab(calibrationPrefab) as GameObject;
            if (calibration == null)
            {
                Debug.LogError($"无法创建 LookDev 校准预制体：{scenePath}");
                return;
            }

            calibration.name = "LookDevCalibration";
            SceneManager.MoveGameObjectToScene(calibration, scene);
        }

        var volumeObject = FindSceneObject("LookDevCalibrationVolume");
        if (volumeObject == null)
        {
            volumeObject = new GameObject("LookDevCalibrationVolume");
            SceneManager.MoveGameObjectToScene(volumeObject, scene);
        }

        var volume = volumeObject.GetComponent<Volume>() ?? volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = -100f;
        volume.weight = 1f;
        volume.sharedProfile = volumeProfile;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void EnsureCalibrationAssets()
    {
        EnsureFolder("Assets/unity-graphics-lab/Shared/Materials");
        EnsureFolder("Assets/unity-graphics-lab/Shared/Textures");
        EnsureFolder("Assets/unity-graphics-lab/Shared/Pipeline/LookDev");

        var chart = AssetDatabase.LoadAssetAtPath<Texture2D>(CalibrationChartTexturePath);
        if (chart == null)
        {
            Debug.LogError($"找不到 Wikimedia 色卡：{CalibrationChartTexturePath}");
            return;
        }

        var ballMaterial = AssetDatabase.LoadAssetAtPath<Material>(CalibrationBallMaterialPath);
        if (ballMaterial == null)
        {
            var litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null)
            {
                Debug.LogError("找不到 URP Lit Shader，无法创建 MatBall 校准材质。");
                return;
            }

            ballMaterial = new Material(litShader) { name = "M_LookDevCalibrationBall" };
            ballMaterial.SetColor("_BaseColor", new Color(0.5f, 0.5f, 0.5f, 1f));
            ballMaterial.SetFloat("_Metallic", 0f);
            ballMaterial.SetFloat("_Smoothness", 0.5f);
            AssetDatabase.CreateAsset(ballMaterial, CalibrationBallMaterialPath);
        }

        var chartMaterial = AssetDatabase.LoadAssetAtPath<Material>(CalibrationChartMaterialPath);
        if (chartMaterial == null)
        {
            var unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null)
            {
                Debug.LogError("找不到 URP Unlit Shader，无法创建色卡材质。");
                return;
            }

            chartMaterial = new Material(unlitShader) { name = "M_LookDevColorChart" };
            chartMaterial.SetColor("_BaseColor", Color.white);
            AssetDatabase.CreateAsset(chartMaterial, CalibrationChartMaterialPath);
        }

        chartMaterial.SetTexture("_BaseMap", chart);
        EditorUtility.SetDirty(chartMaterial);
        ConfigurePreviewPrefabs();

        var volumeProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(CalibrationVolumeProfilePath);
        if (volumeProfile == null)
        {
            volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            volumeProfile.name = "LookDevCalibrationVolumeProfile";
            AssetDatabase.CreateAsset(volumeProfile, CalibrationVolumeProfilePath);
        }

        if (volumeProfile.components == null || volumeProfile.components.Count == 0)
        {
            var tonemapping = volumeProfile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.Neutral);
            AssetDatabase.AddObjectToAsset(tonemapping, volumeProfile);

            var whiteBalance = volumeProfile.Add<WhiteBalance>(true);
            whiteBalance.temperature.Override(0f);
            whiteBalance.tint.Override(0f);
            AssetDatabase.AddObjectToAsset(whiteBalance, volumeProfile);

            var colorAdjustments = volumeProfile.Add<ColorAdjustments>(true);
            colorAdjustments.postExposure.Override(0f);
            AssetDatabase.AddObjectToAsset(colorAdjustments, volumeProfile);
            EditorUtility.SetDirty(volumeProfile);
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(CalibrationPrefabPath) == null)
        {
            var root = new GameObject("LookDevCalibration");
            root.transform.position = new Vector3(3.5f, 0f, 0f);

            var matBallAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/unity-graphics-lab/Shared/Mesh/MatBall.fbx");
            if (matBallAsset != null)
            {
                var ball = PrefabUtility.InstantiatePrefab(matBallAsset) as GameObject;
                if (ball != null)
                {
                    ball.name = "Calibration_MatBall";
                    ball.transform.SetParent(root.transform, false);
                    ball.transform.localPosition = new Vector3(0f, 0.55f, 0f);
                    ball.transform.localScale = Vector3.one * 0.65f;
                    foreach (var renderer in ball.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.sharedMaterial = ballMaterial;
                    }
                }
            }

            var chartObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            chartObject.name = "Calibration_ColorChart";
            UnityEngine.Object.DestroyImmediate(chartObject.GetComponent<Collider>());
            chartObject.transform.SetParent(root.transform, false);
            chartObject.transform.localPosition = new Vector3(0f, -0.65f, 0f);
            chartObject.transform.localScale = new Vector3(1.25f, 0.65f, 1f);
            chartObject.GetComponent<MeshRenderer>().sharedMaterial = chartMaterial;

            PrefabUtility.SaveAsPrefabAsset(root, CalibrationPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
        }

        StripStandaloneCalibrationBall();
        EnsureCalibrationChartUi(chart);
        EnsureMatBallCarrierPrefab();
        EnsureFullscreenQuadCarrierPrefab();
    }

    private static void EnsureCalibrationChartUi(Texture2D chart)
    {
        var contents = PrefabUtility.LoadPrefabContents(CalibrationPrefabPath);
        if (contents == null)
        {
            Debug.LogError($"无法打开 LookDev 校准预制体：{CalibrationPrefabPath}");
            return;
        }

        try
        {
            var oldCharts = new List<GameObject>();
            foreach (var transform in contents.GetComponentsInChildren<Transform>(true))
            {
                if (transform != contents.transform && transform.name == "Calibration_ColorChart")
                {
                    oldCharts.Add(transform.gameObject);
                }
            }

            foreach (var oldChart in oldCharts)
            {
                UnityEngine.Object.DestroyImmediate(oldChart);
            }

            var canvasTransform = contents.transform.Find("LookDevCalibrationCanvas");
            Canvas canvas;
            if (canvasTransform == null)
            {
                var canvasObject = new GameObject("LookDevCalibrationCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvasObject.transform.SetParent(contents.transform, false);
                canvas = canvasObject.GetComponent<Canvas>();
            }
            else
            {
                canvas = canvasTransform.GetComponent<Canvas>() ?? canvasTransform.gameObject.AddComponent<Canvas>();
                if (canvasTransform.GetComponent<RectTransform>() == null)
                {
                    canvasTransform.gameObject.AddComponent<RectTransform>();
                }
                if (canvasTransform.GetComponent<CanvasScaler>() == null)
                {
                    canvasTransform.gameObject.AddComponent<CanvasScaler>();
                }
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvas.transform.localPosition = Vector3.zero;
            canvas.transform.localRotation = Quaternion.identity;
            canvas.transform.localScale = Vector3.one;

            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var chartObject = new GameObject("Calibration_ColorChart", typeof(RectTransform), typeof(RawImage));
            chartObject.transform.SetParent(canvas.transform, false);
            var rect = chartObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-24f, -24f);
            rect.sizeDelta = new Vector2(320f, 221f);

            var rawImage = chartObject.GetComponent<RawImage>();
            rawImage.texture = chart;
            rawImage.color = Color.white;
            rawImage.raycastTarget = false;

            contents.transform.localPosition = Vector3.zero;
            contents.transform.localRotation = Quaternion.identity;
            contents.transform.localScale = Vector3.one;
            PrefabUtility.SaveAsPrefabAsset(contents, CalibrationPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static void StripStandaloneCalibrationBall()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CalibrationPrefabPath);
        if (prefab == null || prefab.transform.Find("Calibration_MatBall") == null)
        {
            return;
        }

        var contents = PrefabUtility.LoadPrefabContents(CalibrationPrefabPath);
        var ball = contents.transform.Find("Calibration_MatBall");
        if (ball != null)
        {
            UnityEngine.Object.DestroyImmediate(ball.gameObject);
            PrefabUtility.SaveAsPrefabAsset(contents, CalibrationPrefabPath);
        }

        PrefabUtility.UnloadPrefabContents(contents);
    }

    private static void ConfigurePreviewPrefabs()
    {
        var matBallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/unity-graphics-lab/Shared/Mesh/MatBall.fbx");
        if (matBallPrefab == null)
        {
            Debug.LogWarning("找不到 MatBall，暂时无法写入预览挡位配置。");
            return;
        }

        foreach (var profilePath in ProfileByScene.Values)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumePreviewSceneProfile>(profilePath);
            if (profile == null)
            {
                continue;
            }

            var serialized = new SerializedObject(profile);
            var modes = serialized.FindProperty("previewModes");
            if (modes == null)
            {
                continue;
            }

            for (var i = 0; i < modes.arraySize; i++)
            {
                var mode = modes.GetArrayElementAtIndex(i);
                var prefab = mode.FindPropertyRelative("previewPrefab");
                var fullscreen = mode.FindPropertyRelative("useFullscreenQuad");
                if (prefab != null && prefab.objectReferenceValue == null &&
                    (fullscreen == null || !fullscreen.boolValue))
                {
                    prefab.objectReferenceValue = matBallPrefab;
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
        }
    }

    private static void EnsureMatBallCarrierPrefab()
    {
        var matBallAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/unity-graphics-lab/Shared/Mesh/MatBall.fbx");
        if (matBallAsset == null)
        {
            return;
        }

        var contents = PrefabUtility.LoadPrefabContents(SharedPrefabPath);
        if (contents.transform.Find("PreviewCarrier_MatBall") == null)
        {
            var carrier = PrefabUtility.InstantiatePrefab(matBallAsset) as GameObject;
            if (carrier != null)
            {
                carrier.name = "PreviewCarrier_MatBall";
                carrier.transform.SetParent(contents.transform, false);
                carrier.transform.localPosition = Vector3.zero;
                carrier.transform.localRotation = Quaternion.identity;
                carrier.transform.localScale = Vector3.one;

                var bounds = default(Bounds);
                var hasBounds = false;
                foreach (var renderer in carrier.GetComponentsInChildren<Renderer>(true))
                {
                    if (!hasBounds)
                    {
                        bounds = renderer.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }

                if (hasBounds)
                {
                    carrier.transform.localPosition -= contents.transform.InverseTransformPoint(bounds.center);
                }

                carrier.SetActive(false);
            }

            PrefabUtility.SaveAsPrefabAsset(contents, SharedPrefabPath);
        }

        PrefabUtility.UnloadPrefabContents(contents);
    }

    private static void EnsureFullscreenQuadCarrierPrefab()
    {
        var quadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        if (quadMesh == null)
        {
            Debug.LogError("找不到 Unity 内置 Quad 网格，无法配置全屏载体。");
            return;
        }

        var contents = PrefabUtility.LoadPrefabContents(SharedPrefabPath);
        if (contents == null)
        {
            return;
        }

        try
        {
            var preview = contents.transform.Find("Preview");
            if (preview == null)
            {
                Debug.LogError($"共享载体预制体缺少 Preview：{SharedPrefabPath}");
                return;
            }

            var filter = preview.GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = preview.gameObject.AddComponent<MeshFilter>();
            }

            filter.sharedMesh = quadMesh;
            PrefabUtility.SaveAsPrefabAsset(contents, SharedPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        var parent = path.Substring(0, path.LastIndexOf('/'));
        var folder = path.Substring(path.LastIndexOf('/') + 1);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folder);
    }

    private static int ValidateScene(string scenePath, string profilePath)
    {
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var errors = new List<string>();
        var profile = AssetDatabase.LoadAssetAtPath<VolumePreviewSceneProfile>(profilePath);
        var root = FindSceneObject("LookDevRoot") ?? FindSceneObject("SceneTemplate");
        var carrierRoot = FindSceneObject("PreviewCarrierTemplate");

        if (profile == null) errors.Add("缺少 Profile");
        if (root == null) errors.Add("缺少 LookDevRoot");
        if (FindSceneObject("Main Camera")?.GetComponent<Camera>() == null) errors.Add("缺少 Main Camera");
        if (carrierRoot == null) errors.Add("缺少 PreviewCarrierTemplate");

        if (profile != null)
        {
            for (var i = 0; i < profile.PreviewModeCount; i++)
            {
                if (profile.GetPreviewMaterial(i) == null)
                {
                    errors.Add($"挡位 {i} 缺少材质");
                }

                if (profile.GetPreviewMesh(i) == null)
                {
                    errors.Add($"挡位 {i} 缺少网格");
                }
            }
        }

        if (carrierRoot != null)
        {
            foreach (var childName in new[]
                     {
                         "Preview",
                         "PreviewCarrier_Sphere",
                         "PreviewCarrier_Cube",
                         "PreviewCarrier_Capsule",
                         "Plane",
                         "PreviewCarrier_MatBall"
                     })
            {
                if (FindChild(carrierRoot.transform, childName) == null)
                {
                    errors.Add($"载体缺少子物体：{childName}");
                }
            }
        }

        if (errors.Count > 0)
        {
            Debug.LogError($"{scenePath}: {string.Join("；", errors)}");
        }

        return errors.Count;
    }

    private static GameObject FindSceneObject(string objectName)
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            var match = FindChild(root.transform, objectName);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private static GameObject FindChild(Transform parent, string objectName)
    {
        if (parent == null)
        {
            return null;
        }

        if (parent.name == objectName)
        {
            return parent.gameObject;
        }

        foreach (Transform child in parent)
        {
            var match = FindChild(child, objectName);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private static void SetObjectReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        var serializedObject = new SerializedObject(target);
        var property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            Debug.LogWarning($"{target.GetType().Name} 缺少序列化字段：{propertyName}");
            return;
        }

        property.objectReferenceValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject[] ResolveModeCarriers(
        VolumePreviewSceneProfile profile,
        GameObject planeCarrier,
        GameObject matBallCarrier)
    {
        var carriers = new GameObject[profile.PreviewModeCount];
        for (var i = 0; i < carriers.Length; i++)
        {
            if (profile.IsPreviewModeSpecial(i))
            {
                carriers[i] = null;
                continue;
            }

            var previewPrefab = profile.GetPreviewPrefab(i);
            if (CarrierMatchesPrefab(planeCarrier, previewPrefab))
            {
                carriers[i] = planeCarrier;
            }
            else if (CarrierMatchesPrefab(matBallCarrier, previewPrefab))
            {
                carriers[i] = matBallCarrier;
            }
            else
            {
                carriers[i] = matBallCarrier;
            }
        }

        return carriers;
    }

    private static bool CarrierMatchesPrefab(GameObject carrier, GameObject previewPrefab)
    {
        if (carrier == null || previewPrefab == null)
        {
            return false;
        }

        var carrierMesh = carrier.GetComponentInChildren<MeshFilter>(true)?.sharedMesh;
        var prefabMesh = previewPrefab.GetComponentInChildren<MeshFilter>(true)?.sharedMesh;
        return carrierMesh != null && carrierMesh == prefabMesh;
    }

    private static void SetObjectArrayReferences(
        UnityEngine.Object target,
        string propertyName,
        IReadOnlyList<GameObject> values)
    {
        var serializedObject = new SerializedObject(target);
        var property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            Debug.LogWarning($"{target.GetType().Name} 缺少序列化字段：{propertyName}");
            return;
        }

        property.arraySize = values?.Count ?? 0;
        for (var i = 0; i < property.arraySize; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }
}
