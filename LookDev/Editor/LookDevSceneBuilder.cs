using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityGraphicsLab.LookDev.Editor
{
    public static class LookDevSceneBuilder
    {
        private enum LookDevKind
        {
            Material,
            ScreenSpace,
            PostProcess
        }

        private enum SceneLayout
        {
            MaterialGrid,
            MaterialThickness,
            ScreenSpaceComposition,
            ScreenSpaceReflection,
            PostProcessMotion,
            Calibration
        }

        private const string Root = "Assets/unity-graphics-lab/LookDev";
        private const string SceneRoot = Root + "/Scenes";
        private const string PrefabRoot = Root + "/Prefabs";
        private const string CalibrationRoot = Root + "/Calibration";
        private const string CalibrationMaterialRoot = CalibrationRoot + "/Materials";
        private const string EnvironmentRoot = Root + "/Environment";
        private const string EnvironmentTexturePath = EnvironmentRoot + "/IndoorEnvironmentHDRI018_16K_HDR.exr";
        private const string EnvironmentSkyboxMaterialPath = EnvironmentRoot + "/IndoorEnvironmentHDRI018_16K_Skybox.mat";
        private const string EnvironmentSphereMaterialPath = EnvironmentRoot + "/IndoorEnvironmentHDRI018_16K_EnvironmentSphere.mat";
        private const string LabelFontPath = EnvironmentRoot + "/SmileySans-Oblique.ttf";
        private const string EnvironmentSpherePrefabPath = PrefabRoot + "/ENV_Sphere.prefab";
        private const string MaterialGridPrefabPath = PrefabRoot + "/MAT_Grid.prefab";
        private const string LightingPrefabPath = PrefabRoot + "/LGT_Rig.prefab";
        private const string CalibrationPrefabPath = PrefabRoot + "/CAL_Kit.prefab";
        private const string MotionPrefabPath = PrefabRoot + "/TGT_Motion.prefab";
        private const string EnvironmentVolumeProfilePath = CalibrationRoot + "/LookDevEnvironmentVolumeProfile.asset";
        private static Font labelFont;
        private static int labelIndex;

        [MenuItem("Tools/LookDev 对照/重建标准场景")]
        public static void RebuildStandardScenes()
        {
            EnsureFolders();
            CreateReusableAssets();
            CreateScene(LookDevKind.Material, "LookDev-RMTest", SceneLayout.MaterialGrid);
            CreateScene(LookDevKind.Material, "LookDev-ThicknessTest", SceneLayout.MaterialThickness);
            CreateScene(LookDevKind.ScreenSpace, "LookDev-DepthTest", SceneLayout.ScreenSpaceComposition);
            CreateScene(LookDevKind.ScreenSpace, "LookDev-ReflectTest", SceneLayout.ScreenSpaceReflection);
            CreateScene(LookDevKind.PostProcess, "LookDev-MotionTest", SceneLayout.PostProcessMotion);
            CreateScene(LookDevKind.Material, "LookDev-ChartTest", SceneLayout.Calibration);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Unity Graphics Lab: standard LookDev scenes rebuilt.");
        }

        private static void CreateScene(LookDevKind kind, string sceneName, SceneLayout layout)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            labelIndex = 0;
            var root = new GameObject("LVD_Root");
            var environment = CreateGroup(root.transform, "ENV_Root");

            CreateEnvironment(environment);
            CreateLighting(environment);
            CreateCamera(environment, kind, layout);

            if (layout == SceneLayout.Calibration)
            {
                var calibration = CreateGroup(root.transform, "STG_Calib");
                CreateCalibration(calibration, new Vector3(0f, 0f, 1.5f), true);
            }
            else
            {
                var stage = CreateGroup(root.transform, GetStageName(layout));
                CreateStage(stage, layout);
            }

            var path = SceneRoot + "/" + sceneName + ".unity";
            EditorSceneManager.SaveScene(scene, path);
        }

        private static string GetStageName(SceneLayout layout)
        {
            switch (layout)
            {
                case SceneLayout.MaterialGrid:
                    return "STG_PBR";
                case SceneLayout.MaterialThickness:
                    return "STG_Thick";
                case SceneLayout.ScreenSpaceComposition:
                    return "STG_Screen";
                case SceneLayout.ScreenSpaceReflection:
                    return "STG_Reflect";
                case SceneLayout.PostProcessMotion:
                    return "STG_Post";
                default:
                    return "STG_Chart";
            }
        }

        private static void CreateStage(Transform parent, SceneLayout layout)
        {
            switch (layout)
            {
                case SceneLayout.MaterialGrid:
                    CreateMaterialGridStage(parent);
                    break;
                case SceneLayout.MaterialThickness:
                    CreateMaterialThicknessStage(parent);
                    break;
                case SceneLayout.ScreenSpaceComposition:
                    CreateGeometryStage(parent);
                    break;
                case SceneLayout.ScreenSpaceReflection:
                    CreateScreenSpaceReflectionStage(parent);
                    break;
                case SceneLayout.PostProcessMotion:
                    CreatePostProcessStage(parent);
                    break;
            }
        }

        private static void CreateEnvironment(Transform parent)
        {
            var skybox = AssetDatabase.LoadAssetAtPath<Material>(EnvironmentSkyboxMaterialPath);
            if (skybox != null)
            {
                RenderSettings.skybox = skybox;
                RenderSettings.ambientMode = AmbientMode.Skybox;
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
                RenderSettings.reflectionIntensity = 1.1f;
            }

            RenderSettings.ambientSkyColor = new Color(0.30f, 0.34f, 0.40f);
            RenderSettings.ambientEquatorColor = new Color(0.17f, 0.20f, 0.25f);
            RenderSettings.ambientGroundColor = new Color(0.075f, 0.07f, 0.065f);
            RenderSettings.ambientIntensity = 1.2f;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentSpherePrefabPath);
            if (prefab != null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                instance.name = "ENV_Sphere";
            }
        }

        private static void CreateLighting(Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LightingPrefabPath);
            if (prefab != null)
            {
                PrefabUtility.InstantiatePrefab(prefab, parent);
                return;
            }

            var lighting = new GameObject("LGT_Rig");
            lighting.transform.SetParent(parent);
            CreateLightingContents(lighting.transform);
        }

        private static void CreateLightingContents(Transform parent)
        {
            var lighting = new GameObject("LGT_Set");
            lighting.transform.SetParent(parent);

            CreateDirectionalLight(lighting.transform, "LGT_Key", new Vector3(50f, -35f, -25f), 1.6f, new Color(1f, 0.93f, 0.82f), true);
            CreateDirectionalLight(lighting.transform, "LGT_Fill", new Vector3(35f, 140f, 25f), 0.6f, new Color(0.72f, 0.82f, 1f), false);
            CreateDirectionalLight(lighting.transform, "LGT_Rim", new Vector3(-25f, 180f, 160f), 0.5f, Color.white, false);

            var ambient = new GameObject("VOL_Env");
            ambient.transform.SetParent(lighting.transform);
            var volume = ambient.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(EnvironmentVolumeProfilePath);
        }

        private static void CreateCamera(Transform parent, LookDevKind kind, SceneLayout layout)
        {
            var cameraRig = CreateGroup(parent, "CAM_Rig");
            var cameraObject = new GameObject("CAM_Main");
            cameraObject.transform.SetParent(cameraRig);
            if (layout == SceneLayout.MaterialGrid)
            {
                cameraObject.transform.position = new Vector3(0f, 2.8f, -9f);
                cameraObject.transform.LookAt(new Vector3(0f, 1.9f, 3f));
            }
            else if (layout == SceneLayout.ScreenSpaceComposition)
            {
                cameraObject.transform.position = new Vector3(0f, 3.4f, -11f);
                cameraObject.transform.LookAt(new Vector3(0f, 1.5f, 4f));
            }
            else if (layout == SceneLayout.PostProcessMotion)
            {
                cameraObject.transform.position = new Vector3(0f, 3f, -10f);
                cameraObject.transform.LookAt(new Vector3(0f, 1.6f, 3.2f));
            }
            else
            {
                cameraObject.transform.position = new Vector3(0f, 2.7f, -8.5f);
                cameraObject.transform.LookAt(new Vector3(0f, 1.4f, 3.2f));
            }
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = layout == SceneLayout.ScreenSpaceComposition ? 50f : layout == SceneLayout.PostProcessMotion ? 43f : 44f;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.06f, 1f);
            camera.allowHDR = true;
            camera.allowMSAA = true;
            var cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = true;
            cameraData.renderShadows = true;
        }

        private static void CreateCalibration(Transform parent, Vector3 position, bool active)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CalibrationPrefabPath);
            if (prefab != null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                instance.name = "CAL_Kit";
                instance.transform.localPosition = position;
                instance.SetActive(active);
                return;
            }

            var calibration = new GameObject("CAL_Kit");
            calibration.transform.SetParent(parent);
            calibration.transform.localPosition = position;
            calibration.SetActive(active);
            CreateCalibrationContents(calibration.transform);
        }

        private static void CreateCalibrationContents(Transform parent)
        {
            CreateCalibrationBall(parent, "REF_Gray", new Vector3(-3.2f, 1f, 0f), "LookDev_CalibrationGray.mat");
            CreateCalibrationBall(parent, "REF_Mirror", new Vector3(-1.6f, 1f, 0f), "LookDev_CalibrationMirror.mat");
            CreateCalibrationBall(parent, "REF_Rough", new Vector3(0f, 1f, 0f), "LookDev_CalibrationRough.mat");
            CreateLabel(parent, "灰球", new Vector3(-3.2f, 1.8f, 0f), new Color(0.8f, 0.9f, 1f), 0.7f);
            CreateLabel(parent, "镜面球", new Vector3(-1.6f, 1.8f, 0f), new Color(0.8f, 0.9f, 1f), 0.7f);
            CreateLabel(parent, "粗糙球", new Vector3(0f, 1.8f, 0f), new Color(0.8f, 0.9f, 1f), 0.7f);

            var chartTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/unity-graphics-lab/LookDev/Calibration/Textures/T_LookDevColorChart.png");
            var chartMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/unity-graphics-lab/LookDev/Calibration/Materials/M_LookDevColorChart.mat");
            if (chartTexture != null && chartMaterial != null)
            {
                var chart = GameObject.CreatePrimitive(PrimitiveType.Cube);
                chart.name = "REF_Chart";
                chart.transform.SetParent(parent);
                chart.transform.position = new Vector3(2.2f, 1.8f, 0.5f);
                chart.transform.localScale = new Vector3(1.7f, 1.05f, 0.05f);
                chart.GetComponent<Renderer>().sharedMaterial = chartMaterial;
                CreateLabel(parent, "颜色卡", new Vector3(2.2f, 2.55f, 0.5f), new Color(1f, 0.8f, 0.35f), 0.7f);
            }
        }

        private static void CreateMaterialGridStage(Transform parent)
        {
            var surface = CreateGroup(parent, "STG_Surface");
            var grid = CreateGroup(parent, "TGT_Grid");
            CreateFloor(surface, new Vector3(0f, -0.05f, 2f), new Vector3(12f, 0.1f, 8f));
            CreateMaterialGrid(grid);
        }

        private static void CreateMaterialThicknessStage(Transform parent)
        {
            var surface = CreateGroup(parent, "STG_Surface");
            var thickness = CreateGroup(parent, "TGT_Thick");
            CreateFloor(surface, new Vector3(0f, -0.05f, 2.5f), new Vector3(10f, 0.1f, 8f));
            CreateCube(thickness, "REF_Thin", new Vector3(-1.7f, 1.1f, 3.4f), new Vector3(1f, 2.2f, 0.12f), 0.2f);
            CreateCube(thickness, "REF_Medium", new Vector3(0f, 1.1f, 3.4f), new Vector3(1f, 2.2f, 0.32f), 0.3f);
            CreateCube(thickness, "REF_Thick", new Vector3(1.7f, 1.1f, 3.4f), new Vector3(1f, 2.2f, 0.65f), 0.4f);
            CreateCube(thickness, "REF_Wedge", new Vector3(2.7f, 0.8f, 3.4f), new Vector3(0.35f, 1.6f, 1.6f), 0.4f);
            CreateLabel(thickness, "薄", new Vector3(-1.7f, 2.35f, 3.4f), new Color(1f, 0.8f, 0.55f), 0.65f);
            CreateLabel(thickness, "中", new Vector3(0f, 2.35f, 3.4f), new Color(1f, 0.8f, 0.55f), 0.65f);
            CreateLabel(thickness, "厚", new Vector3(1.7f, 2.35f, 3.4f), new Color(1f, 0.8f, 0.55f), 0.65f);
            CreateLabel(thickness, "厚度 / SSS", new Vector3(0.5f, 3.05f, 3.4f), new Color(1f, 0.8f, 0.55f), 0.85f);
        }

        private static void CreateMaterialGrid(Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MaterialGridPrefabPath);
            if (prefab != null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                instance.name = "MAT_Grid";
            }
        }

        private static void CreateGeometryStage(Transform parent)
        {
            var geometry = CreateGroup(parent, "GEO_Screen");
            CreateFloor(geometry, new Vector3(0f, -0.05f, 2.5f), new Vector3(14f, 0.1f, 12f));
            var wallCorner = CreateGroup(geometry, "GEO_Corner");
            CreateCube(wallCorner, "GEO_Wall_Back", new Vector3(0f, 2.5f, 7f), new Vector3(14f, 5f, 0.2f), 0.15f);
            CreateCube(wallCorner, "GEO_Wall_Side", new Vector3(-5.5f, 2.5f, 2.5f), new Vector3(0.2f, 5f, 9f), 0.15f);
            CreateLabel(wallCorner, "墙角 / 接收面", new Vector3(0f, 4.65f, 6.8f), new Color(0.8f, 0.9f, 1f), 0.8f);
            var steps = CreateGroup(geometry, "GEO_Steps");
            for (var i = 0; i < 5; i++)
                CreateCube(steps, "GEO_Step_" + i, new Vector3(-2.8f + i * 1.15f, 0.2f + i * 0.4f, 4.8f), new Vector3(1f, 0.4f + i * 0.8f, 1.8f), 0.25f);
            CreateLabel(steps, "台阶 / 深度层", new Vector3(-0.4f, 3.55f, 4.8f), new Color(1f, 0.8f, 0.55f), 0.8f);
            CreateCube(geometry, "GEO_Occluder", new Vector3(2.4f, 0.7f, 2.6f), new Vector3(1.4f, 1.4f, 1.4f), 0.2f);
            CreateCube(geometry, "GEO_Normal", new Vector3(3.7f, 1.5f, 4.6f), new Vector3(0.7f, 3f, 0.7f), 0.3f);
            CreateLabel(geometry, "深度 / 法线", new Vector3(3.2f, 3.25f, 4.6f), new Color(0.8f, 0.9f, 1f), 0.75f);
        }

        private static void CreateScreenSpaceReflectionStage(Transform parent)
        {
            var geometry = CreateGroup(parent, "GEO_Reflect");
            var target = CreateGroup(parent, "TGT_Reflect");
            CreateFloor(geometry, new Vector3(0f, -0.05f, 2.5f), new Vector3(12f, 0.1f, 9f));
            CreateCube(geometry, "GEO_Wall_Back", new Vector3(0f, 2.5f, 6.5f), new Vector3(12f, 5f, 0.2f), 0.3f);
            var reflectionCard = CreateCube(target, "TGT_Card", new Vector3(1.1f, 0.8f, 4.2f), new Vector3(1.4f, 1.6f, 0.12f), 0.1f);
            reflectionCard.GetComponent<Renderer>().sharedMaterial = CreateRuntimeMaterial("MAT_Card", new Color(0.04f, 0.2f, 0.75f), 0.35f, 0.65f);
            CreateLabel(target, "反射卡", new Vector3(1.1f, 1.75f, 4.2f), new Color(0.55f, 0.7f, 1f), 0.8f);
            var reflectionSphere = CreateMaterialBall(target, "TGT_Reflector", new Vector3(-0.8f, 0.8f, 3.2f), 0.1f, 0.3f, 0.7f);
            reflectionSphere.GetComponent<Renderer>().sharedMaterial = CreateRuntimeMaterial("MAT_Reflector", new Color(0.85f, 0.15f, 0.03f), 0.15f, 0.35f);
            CreateLabel(target, "被反射物体", new Vector3(-0.8f, 1.55f, 3.2f), new Color(1f, 0.55f, 0.4f), 0.75f);
        }

        private static void CreatePostProcessStage(Transform parent)
        {
            var composition = CreateGroup(parent, "GEO_Depth");
            var targets = CreateGroup(parent, "TGT_Post");
            CreateFloor(composition, new Vector3(0f, -0.05f, 3f), new Vector3(12f, 0.1f, 12f));
            CreateCube(composition, "GEO_Fore", new Vector3(-2.8f, 1.2f, 1.2f), new Vector3(1.8f, 2.4f, 1.8f), 0.2f);
            CreateCube(composition, "GEO_Back", new Vector3(2.8f, 1.5f, 6f), new Vector3(2.2f, 3f, 2.2f), 0.8f);
            CreateLabel(composition, "前景", new Vector3(-2.8f, 2.65f, 1.2f), new Color(0.8f, 0.9f, 1f), 0.75f);
            CreateLabel(composition, "后景", new Vector3(2.8f, 3.25f, 6f), new Color(0.8f, 0.9f, 1f), 0.75f);
            var staticTarget = CreateMaterialBall(targets, "TGT_Static", new Vector3(-1.4f, 1.1f, 4.8f), 0.1f, 0.35f, 0.65f, "static");
            staticTarget.GetComponent<Renderer>().sharedMaterial = CreateRuntimeMaterial("MAT_Static", new Color(0.7f, 0.16f, 0.04f), 0.1f, 0.35f);
            CreateLabel(targets, "静态目标", new Vector3(-1.9f, 2.2f, 4.8f), new Color(1f, 0.55f, 0.4f), 0.8f);
            CreateMotionObject(targets, new Vector3(0f, 1.1f, 2.5f));
            CreateLabel(targets, "中景 / 循环运动", new Vector3(0.8f, 2.05f, 2.5f), new Color(0.55f, 0.7f, 1f), 0.75f);
        }

        private static void CreateMotionObject(Transform parent, Vector3 position)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MotionPrefabPath);
            if (prefab != null)
            {
                var motionPrefab = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                motionPrefab.name = "TGT_Motion";
                motionPrefab.transform.position = position;
                return;
            }

            CreateMotionObjectContents(parent, position);
        }

        private static void CreateMotionObjectContents(Transform parent, Vector3 position)
        {
            var motion = CreateCube(parent, "TGT_Motion", position, Vector3.one * 1.3f, 0.3f);
            motion.AddComponent<UnityGraphicsLab.LookDev.LookDevMotionDriver>();
            var target = motion.AddComponent<UnityGraphicsLab.LookDev.LookDevCaptureTarget>();
            target.SetTargetId("motion");
        }

        private static GameObject CreateFloor(Transform parent, Vector3 position, Vector3 scale)
        {
            return CreateCube(parent, "GEO_Floor", position, scale, 0.55f);
        }

        private static GameObject CreateCube(Transform parent, string name, Vector3 position, Vector3 scale, float roughness)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent);
            cube.transform.position = position;
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = CreateRuntimeMaterial(name + "_Material", new Color(0.35f, 0.38f, 0.42f), 0.15f, roughness);
            return cube;
        }

        private static GameObject CreateMaterialBall(Transform parent, string name, Vector3 position, float metallic, float smoothness, float roughness, string targetId = null)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = name;
            sphere.transform.SetParent(parent);
            sphere.transform.position = position;
            sphere.GetComponent<Renderer>().sharedMaterial = CreateRuntimeMaterial(name + "_Material", new Color(0.65f, 0.68f, 0.72f), metallic, Mathf.Clamp01(smoothness > 0f ? smoothness : 1f - roughness));
            if (!string.IsNullOrEmpty(targetId))
            {
                var target = sphere.AddComponent<UnityGraphicsLab.LookDev.LookDevCaptureTarget>();
                target.SetTargetId(targetId);
            }

            return sphere;
        }

        private static void CreateLabel(Transform parent, string text, Vector3 position, Color color, float size = 1f)
        {
            var label = new GameObject("LBL_" + labelIndex++);
            label.transform.SetParent(parent);
            // Keep annotations in front of the referenced geometry so the capture camera can read them.
            label.transform.position = position + Vector3.back * 0.2f;
            label.transform.rotation = Quaternion.identity;

            var textMesh = label.AddComponent<TextMesh>();
            textMesh.text = text;
            textMesh.fontSize = 48;
            textMesh.characterSize = 0.06f * size;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.fontStyle = FontStyle.Bold;
            textMesh.color = color;
            // Assign the imported font after text data exists so Unity rebuilds the TextMesh cache.
            textMesh.font = GetLabelFont();
            textMesh.font.RequestCharactersInTexture(text, textMesh.fontSize, textMesh.fontStyle);
            EditorUtility.SetDirty(textMesh);

            var meshRenderer = label.GetComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        private static Font GetLabelFont()
        {
            if (labelFont != null)
                return labelFont;

            AssetDatabase.ImportAsset(LabelFontPath, ImportAssetOptions.ForceSynchronousImport);
            labelFont = AssetDatabase.LoadAssetAtPath<Font>(LabelFontPath);
            if (labelFont != null)
                return labelFont;

            var installedFonts = Font.GetOSInstalledFontNames();
            var preferredNames = new[] { "Microsoft YaHei", "微软雅黑", "Microsoft YaHei UI", "SimHei", "黑体" };
            for (var i = 0; i < preferredNames.Length && labelFont == null; i++)
            {
                for (var j = 0; j < installedFonts.Length; j++)
                {
                    if (installedFonts[j].Contains(preferredNames[i]))
                    {
                        labelFont = Font.CreateDynamicFontFromOSFont(installedFonts[j], 64);
                        break;
                    }
                }
            }

            if (labelFont == null)
                labelFont = Font.CreateDynamicFontFromOSFont("Arial", 64);
            return labelFont;
        }

        private static void CreateDirectionalLight(Transform parent, string name, Vector3 rotation, float intensity, Color color, bool castsShadows)
        {
            var lightObject = new GameObject(name);
            lightObject.transform.SetParent(parent);
            lightObject.transform.rotation = Quaternion.Euler(rotation);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.shadows = castsShadows ? LightShadows.Soft : LightShadows.None;
        }

        private static Material CreateRuntimeMaterial(string name, Color color, float metallic, float smoothness, bool emission = false)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (emission && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color);
            }
            return material;
        }

        private static void CreateReusableAssets()
        {
            CreateEnvironmentAssets();
            CreateEnvironmentVolumeProfile();
            CreateCalibrationMaterials();
            CreateLightingPrefab();
            CreateCalibrationPrefab();
            CreateMaterialGridPrefab();
            CreateMotionTargetPrefab();
        }

        private static void CreateEnvironmentAssets()
        {
            AssetDatabase.ImportAsset(EnvironmentTexturePath, ImportAssetOptions.ForceSynchronousImport);
            var hdrTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(EnvironmentTexturePath);
            var sphereShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (hdrTexture == null || sphereShader == null)
            {
                Debug.LogWarning("Unity Graphics Lab: HDRI environment assets are not ready yet.");
                return;
            }

            var skybox = AssetDatabase.LoadAssetAtPath<Material>(EnvironmentSkyboxMaterialPath);
            if (skybox == null)
            {
                skybox = new Material(Shader.Find("Skybox/Panoramic")) { name = "IndoorEnvironmentHDRI018_16K_Skybox" };
                AssetDatabase.CreateAsset(skybox, EnvironmentSkyboxMaterialPath);
            }
            skybox.SetTexture("_MainTex", hdrTexture);
            skybox.SetColor("_Tint", Color.white);
            skybox.SetFloat("_Exposure", 0f);
            skybox.SetFloat("_Rotation", 0f);
            EditorUtility.SetDirty(skybox);

            var sphereMaterial = AssetDatabase.LoadAssetAtPath<Material>(EnvironmentSphereMaterialPath);
            if (sphereMaterial == null)
            {
                sphereMaterial = new Material(sphereShader) { name = "IndoorEnvironmentHDRI018_16K_EnvironmentSphere" };
                AssetDatabase.CreateAsset(sphereMaterial, EnvironmentSphereMaterialPath);
            }
            sphereMaterial.shader = sphereShader;
            sphereMaterial.SetTexture("_BaseMap", hdrTexture);
            sphereMaterial.SetColor("_BaseColor", new Color(1.3f, 1.3f, 1.3f, 1f));
            sphereMaterial.SetFloat("_Cull", 1f);
            sphereMaterial.SetFloat("_ZWrite", 0f);
            EditorUtility.SetDirty(sphereMaterial);

            CreateEnvironmentSpherePrefab(sphereMaterial);
        }

        private static void CreateEnvironmentSpherePrefab(Material sphereMaterial)
        {
            var root = new GameObject("ENV_Sphere");
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "ENV_Mesh";
            sphere.transform.SetParent(root.transform);
            sphere.transform.localPosition = Vector3.zero;
            sphere.transform.localScale = Vector3.one * 100f;

            var renderer = sphere.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = sphereMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.allowOcclusionWhenDynamic = false;
            var collider = sphere.GetComponent<Collider>();
            if (collider != null)
                Object.DestroyImmediate(collider);

            PrefabUtility.SaveAsPrefabAsset(root, EnvironmentSpherePrefabPath);
            Object.DestroyImmediate(root);
        }

        private static void CreateEnvironmentVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(EnvironmentVolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, EnvironmentVolumeProfilePath);
            }

            var tonemapping = GetOrCreateVolumeComponent<Tonemapping>(profile);
            tonemapping.mode.value = TonemappingMode.Neutral;
            var colorAdjustments = GetOrCreateVolumeComponent<ColorAdjustments>(profile);
            colorAdjustments.postExposure.value = 0.5f;
            colorAdjustments.contrast.value = 0f;
            var whiteBalance = GetOrCreateVolumeComponent<WhiteBalance>(profile);
            whiteBalance.temperature.value = 0f;
            whiteBalance.tint.value = 0f;
            EditorUtility.SetDirty(profile);
        }

        private static T GetOrCreateVolumeComponent<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var component = profile.components.Find(item => item is T) as T;
            if (component != null)
                return component;

            component = profile.Add<T>(true);
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        private static void CreateCalibrationMaterials()
        {
            CreateCalibrationMaterial("LookDev_CalibrationGray.mat", new Color(0.65f, 0.68f, 0.72f), 0f, 0.5f);
            CreateCalibrationMaterial("LookDev_CalibrationMirror.mat", new Color(0.65f, 0.68f, 0.72f), 1f, 1f);
            CreateCalibrationMaterial("LookDev_CalibrationRough.mat", new Color(0.65f, 0.68f, 0.72f), 0f, 0.1f);
            CreateCalibrationMaterial("LookDev_MotionTarget.mat", new Color(0.08f, 0.32f, 0.8f), 0.15f, 0.3f);
        }

        private static void CreateCalibrationMaterial(string fileName, Color color, float metallic, float smoothness)
        {
            var path = CalibrationMaterialRoot + "/" + fileName;
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = CreateRuntimeMaterial(fileName.Replace(".mat", string.Empty), color, metallic, smoothness);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                if (material.HasProperty("_Color")) material.SetColor("_Color", color);
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
                EditorUtility.SetDirty(material);
            }
        }

        private static void CreateCalibrationBall(Transform parent, string name, Vector3 position, string materialFileName)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = name;
            sphere.transform.SetParent(parent);
            sphere.transform.position = position;
            sphere.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(CalibrationMaterialRoot + "/" + materialFileName);
        }

        private static void CreateLightingPrefab()
        {
            var root = new GameObject("LGT_Rig");
            CreateLightingContents(root.transform);
            PrefabUtility.SaveAsPrefabAsset(root, LightingPrefabPath);
            Object.DestroyImmediate(root);
        }

        private static void CreateCalibrationPrefab()
        {
            labelIndex = 0;
            var root = new GameObject("CAL_Kit");
            CreateCalibrationContents(root.transform);
            PrefabUtility.SaveAsPrefabAsset(root, CalibrationPrefabPath);
            Object.DestroyImmediate(root);
        }

        private static void CreateMaterialGridPrefab()
        {
            labelIndex = 0;
            var root = new GameObject("MAT_Grid");
            for (var row = 0; row < 5; row++)
            {
                var metallic = row / 4f;
                for (var column = 0; column < 5; column++)
                {
                    var roughness = column / 4f;
                    var gridColor = new Color(0.8f, 0.06f, 0.035f);
                    var materialPath = CalibrationMaterialRoot + "/Grid_Metallic_" + row + "_Roughness_" + column + ".mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (material == null)
                    {
                        material = CreateRuntimeMaterial(
                            "Grid_Metallic_" + row + "_Roughness_" + column,
                            gridColor,
                            metallic,
                            1f - roughness);
                        AssetDatabase.CreateAsset(material, materialPath);
                    }
                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", gridColor);
                    if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
                    if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 1f - roughness);
                    if (material.HasProperty("_EmissionColor"))
                    {
                        material.EnableKeyword("_EMISSION");
                        material.SetColor("_EmissionColor", new Color(0.18f, 0.009f, 0.0045f));
                    }
                    EditorUtility.SetDirty(material);

                    var ball = CreateMaterialBall(
                        root.transform,
                        "TGT_M" + row + "_R" + column,
                        new Vector3(-1.8f + column * 0.9f, 0.6f + row * 0.9f, 2.4f),
                        metallic,
                        1f - roughness,
                        roughness);
                    ball.GetComponent<Renderer>().sharedMaterial = material;
                    ball.transform.localScale = Vector3.one * 0.82f;
                }
            }

            for (var column = 0; column < 5; column++)
            {
                var roughness = column / 4f;
                CreateLabel(root.transform, "粗糙度 " + roughness.ToString("0.00"), new Vector3(-1.8f + column * 0.9f, 5.05f, 2.4f), new Color(0.75f, 0.9f, 1f), 0.55f);
            }

            for (var row = 0; row < 5; row++)
            {
                var metallic = row / 4f;
                CreateLabel(root.transform, "金属度 " + metallic.ToString("0.00"), new Vector3(-2.65f, 0.6f + row * 0.9f, 2.4f), new Color(0.75f, 0.9f, 1f), 0.55f);
            }

            CreateLabel(root.transform, "粗糙度 ->", new Vector3(0f, 5.55f, 2.4f), new Color(0.75f, 0.9f, 1f), 0.75f);

            PrefabUtility.SaveAsPrefabAsset(root, MaterialGridPrefabPath);
            Object.DestroyImmediate(root);
        }

        private static void CreateMotionTargetPrefab()
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "TGT_Motion";
            root.transform.localScale = Vector3.one * 1.3f;
            var renderer = root.GetComponent<Renderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(CalibrationMaterialRoot + "/LookDev_MotionTarget.mat");
            root.AddComponent<UnityGraphicsLab.LookDev.LookDevMotionDriver>();
            var target = root.AddComponent<UnityGraphicsLab.LookDev.LookDevCaptureTarget>();
            target.SetTargetId("motion");
            PrefabUtility.SaveAsPrefabAsset(root, MotionPrefabPath);
            Object.DestroyImmediate(root);
        }

        private static void EnsureFolders()
        {
            EnsureFolder(Root);
            EnsureFolder(SceneRoot);
            EnsureFolder(PrefabRoot);
            EnsureFolder(CalibrationRoot);
            EnsureFolder(CalibrationMaterialRoot);
            EnsureFolder(EnvironmentRoot);
        }

        private static Transform CreateGroup(Transform parent, string name)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent);
            return group.transform;
        }

        private static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
