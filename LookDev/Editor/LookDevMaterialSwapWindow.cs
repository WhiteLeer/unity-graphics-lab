using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityGraphicsLab.LookDev.Editor
{
    public sealed class LookDevMaterialSwapWindow : EditorWindow
    {
        private enum ParameterMode
        {
            None,
            Chart,
            MetallicRoughnessGrid
        }

        private sealed class SceneContract
        {
            public string DisplayName;
            public string SceneName;
            public string Description;
            public ParameterMode ParameterMode;
            public string[] TargetNames;
        }

        private sealed class SwapRecord
        {
            public Renderer Renderer;
            public Material[] OriginalMaterials;
            public readonly List<Material> TemporaryMaterials = new List<Material>();
        }

        private static readonly SceneContract[] Contracts =
        {
            new SceneContract
            {
                DisplayName = "通用材质测试",
                SceneName = "LookDev-ChartTest",
                Description = "将测试材质应用到默认球、灰球、镜面球和粗糙球；后三者分别写入金属度/粗糙度。颜色卡保持不变。",
                ParameterMode = ParameterMode.Chart,
                TargetNames = new[] { "REF_TEST", "REF_Gray", "REF_Mirror", "REF_Rough" }
            },
            new SceneContract
            {
                DisplayName = "金属／粗糙度测试",
                SceneName = "LookDev-RMTest",
                Description = "将测试材质复制到 5x5 网格，每个球根据所在行列写入固定的金属度和粗糙度。",
                ParameterMode = ParameterMode.MetallicRoughnessGrid,
                TargetNames = new string[0]
            },
            new SceneContract
            {
                DisplayName = "厚度测试",
                SceneName = "LookDev-ThicknessTest",
                Description = "只替换薄、中、厚片和厚度楔形，地面和环境保持默认。",
                ParameterMode = ParameterMode.None,
                TargetNames = new[] { "REF_Thin", "REF_Medium", "REF_Thick", "REF_Wedge" }
            },
            new SceneContract
            {
                DisplayName = "深度测试",
                SceneName = "LookDev-DepthTest",
                Description = "只替换台阶、遮挡块和法线柱，墙角与地面保持默认。",
                ParameterMode = ParameterMode.None,
                TargetNames = new[] { "GEO_Step_0", "GEO_Step_1", "GEO_Step_2", "GEO_Step_3", "GEO_Step_4", "GEO_Occluder", "GEO_Normal" }
            },
            new SceneContract
            {
                DisplayName = "反射测试",
                SceneName = "LookDev-ReflectTest",
                Description = "只替换反射卡和被反射球，地面与后墙保持默认。",
                ParameterMode = ParameterMode.None,
                TargetNames = new[] { "TGT_Card", "TGT_Reflector" }
            },
            new SceneContract
            {
                DisplayName = "运动测试",
                SceneName = "LookDev-MotionTest",
                Description = "只替换静态目标和循环运动目标，前景与后景深度锚点保持默认。",
                ParameterMode = ParameterMode.None,
                TargetNames = new[] { "TGT_Static", "TGT_Motion" }
            }
        };

        private static readonly Dictionary<string, SwapRecord> ActiveSwaps = new Dictionary<string, SwapRecord>();
        private const string SceneRoot = "Assets/unity-graphics-lab/LookDev/Scenes";

        private Material testMaterial;
        private SceneContract currentContract;
        private int contractIndex;
        private float[] chartMetallic = { 0f, 1f, 0f };
        private float[] chartRoughness = { 0.5f, 0f, 0.9f };
        private string statusMessage;
        private MessageType statusType = MessageType.Info;

        [MenuItem("Tools/LookDev 对照")]
        private static void Open()
        {
            GetWindow<LookDevMaterialSwapWindow>("LookDev Material Swap");
        }

        private void OnEnable()
        {
            var activeScene = SceneManager.GetActiveScene();
            var activeIndex = FindContractIndex(activeScene.name);
            contractIndex = activeIndex < 0 ? 0 : activeIndex;
            currentContract = Contracts[contractIndex];
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("LOOKDEV 材质测试", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "选择测试类型后，工具会直接使用对应的 LookDev 场景和固定测试契约。不会修改环境、灯光、相机或语义辅助物件。",
                MessageType.Info);

            var displayNames = new string[Contracts.Length];
            for (var i = 0; i < Contracts.Length; i++) displayNames[i] = Contracts[i].DisplayName;
            var nextContractIndex = EditorGUILayout.Popup("测试类型", contractIndex, displayNames);
            if (nextContractIndex != contractIndex)
            {
                contractIndex = nextContractIndex;
                currentContract = Contracts[contractIndex];
                statusMessage = "已选择：" + currentContract.DisplayName;
                statusType = MessageType.Info;
            }

            currentContract = Contracts[contractIndex];
            EditorGUILayout.LabelField("对应场景", currentContract.SceneName, EditorStyles.miniLabel);
            EditorGUILayout.LabelField(currentContract.Description, EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(8f);
            DrawParameterControls();

            EditorGUILayout.Space(4f);
            testMaterial = (Material)EditorGUILayout.ObjectField("测试材质", testMaterial, typeof(Material), false);

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("打开测试场景", GUILayout.Height(28f))) OpenLookDevScene();
                using (new EditorGUI.DisabledScope(testMaterial == null))
                {
                    if (GUILayout.Button("打开并应用", GUILayout.Height(28f))) ApplySelectedContract();
                }
            }

            using (new EditorGUI.DisabledScope(!HasActiveLookDevScene()))
            {
                if (GUILayout.Button("恢复当前场景默认材质", GUILayout.Height(24f))) RestoreCurrentScene();
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("应用只修改当前场景内的 Renderer，不会修改测试材质资产；确认结果后再手动保存场景。", EditorStyles.wordWrappedMiniLabel);
            DrawStatus();
        }

        private void DrawParameterControls()
        {
            if (currentContract.ParameterMode == ParameterMode.None) return;

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.VerticalScope("box"))
            {
                if (currentContract.ParameterMode == ParameterMode.Chart)
                {
                    EditorGUILayout.LabelField("校准参数", EditorStyles.boldLabel);
                    DrawChartParameterRow(0, "灰球");
                    DrawChartParameterRow(1, "镜面球");
                    DrawChartParameterRow(2, "粗糙球");
                }
                else
                {
                    EditorGUILayout.LabelField("网格参数", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("行：金属度 0.00 -> 1.00；列：粗糙度 0.00 -> 1.00", EditorStyles.miniLabel);
                }
            }
        }

        private void DrawChartParameterRow(int index, string label)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(55f));
                chartMetallic[index] = EditorGUILayout.Slider("金属度", chartMetallic[index], 0f, 1f);
                chartRoughness[index] = EditorGUILayout.Slider("粗糙度", chartRoughness[index], 0f, 1f);
            }
        }

        private void OpenLookDevScene()
        {
            var scenePath = SceneRoot + "/" + currentContract.SceneName + ".unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                ShowError("找不到对应的 LookDev 场景：\n" + scenePath, "场景缺失");
                return;
            }

            if (SceneManager.GetActiveScene().path != scenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }

            statusMessage = "已进入 " + currentContract.DisplayName + "。";
            statusType = MessageType.Info;
        }

        private void ApplySelectedContract()
        {
            if (!IsSelectedSceneActive())
            {
                OpenLookDevScene();
                if (!IsSelectedSceneActive()) return;
            }

            ApplyToContract();
        }

        private void ApplyToContract()
        {
            if (testMaterial == null || currentContract == null) return;
            if (testMaterial.shader == null)
            {
                ShowError("测试材质没有有效 Shader，无法应用。", "材质错误");
                return;
            }

            var targets = ResolveTargets(currentContract);
            if (targets.Count == 0)
            {
                ShowError("当前场景没有找到契约要求的测试目标。请确认场景没有被重命名或重建。", "LookDev 目标缺失");
                return;
            }

            if (targets.Count != GetExpectedTargetCount(currentContract))
            {
                ShowError("LookDev 测试目标不完整，已阻止应用，避免得到不完整的对比图。", "LookDev 目标不完整");
                return;
            }

            if (currentContract.ParameterMode != ParameterMode.None && !ValidateParameterProperties(testMaterial))
                return;

            RestoreCurrentSceneInternal(false);
            foreach (var target in targets)
            {
                var record = new SwapRecord
                {
                    Renderer = target,
                    OriginalMaterials = (Material[])target.sharedMaterials.Clone()
                };

                var useDirectMaterial = currentContract.SceneName == "LookDev-ChartTest" && target.gameObject.name == "REF_TEST";
                var replacement = useDirectMaterial ? testMaterial : new Material(testMaterial);
                if (!useDirectMaterial)
                {
                    replacement.name = "LVD_TMP_" + target.gameObject.name + "_" + testMaterial.name;
                    record.TemporaryMaterials.Add(replacement);
                }

                if (currentContract.ParameterMode == ParameterMode.Chart && target.gameObject.name != "REF_TEST")
                {
                    var index = GetChartIndex(target.gameObject.name);
                    SetMaterialParameters(replacement, chartMetallic[index], chartRoughness[index]);
                }
                else if (currentContract.ParameterMode == ParameterMode.MetallicRoughnessGrid)
                {
                    var row = GetGridRow(target.gameObject.name);
                    var column = GetGridColumn(target.gameObject.name);
                    SetMaterialParameters(replacement, row / 4f, column / 4f);
                }

                var materials = target.sharedMaterials;
                for (var i = 0; i < materials.Length; i++) materials[i] = replacement;
                Undo.RecordObject(target, "Apply LookDev test material");
                target.sharedMaterials = materials;
                ActiveSwaps[GetRendererKey(target)] = record;
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            SetStatus("已应用到 " + targets.Count + " 个固定测试目标。", MessageType.Info);
        }

        private bool ValidateParameterProperties(Material material)
        {
            var missing = new List<string>();
            if (!material.HasProperty("_Metallic")) missing.Add("金属度 _Metallic");
            if (!HasRoughnessProperty(material)) missing.Add("粗糙度 _Roughness 或平滑度 _Smoothness");

            if (missing.Count == 0) return true;

            ShowError(
                "测试材质的 Shader 缺少 LookDev 参数：\n- " + string.Join("\n- ", missing.ToArray()) +
                "\n\n该场景需要这些属性来生成可比较的参数结果。",
                "Shader 参数不完整");
            return false;
        }

        private static bool HasRoughnessProperty(Material material)
        {
            return material.HasProperty("_Roughness") || material.HasProperty("_Smoothness") || material.HasProperty("_Glossiness");
        }

        private static void SetMaterialParameters(Material material, float metallic, float roughness)
        {
            material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Roughness"))
                material.SetFloat("_Roughness", roughness);
            else if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 1f - roughness);
            else
                material.SetFloat("_Glossiness", 1f - roughness);
        }

        private void RestoreCurrentScene()
        {
            RestoreCurrentSceneInternal(true);
        }

        private void RestoreCurrentSceneInternal(bool showStatus)
        {
            var scenePath = SceneManager.GetActiveScene().path;
            var keys = new List<string>(ActiveSwaps.Keys);
            var restored = 0;
            foreach (var key in keys)
            {
                if (!key.StartsWith(scenePath + "|")) continue;

                var record = ActiveSwaps[key];
                if (record.Renderer != null)
                {
                    Undo.RecordObject(record.Renderer, "Restore LookDev default materials");
                    record.Renderer.sharedMaterials = (Material[])record.OriginalMaterials.Clone();
                    restored++;
                }

                foreach (var temporary in record.TemporaryMaterials)
                {
                    if (temporary != null) DestroyImmediate(temporary);
                }

                ActiveSwaps.Remove(key);
            }

            if (restored > 0) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            if (showStatus)
                SetStatus(restored == 0 ? "当前场景没有需要恢复的替换。" : "已恢复默认材质。", MessageType.Info);
        }

        private List<Renderer> ResolveTargets(SceneContract contract)
        {
            var targets = new List<Renderer>();
            if (contract.ParameterMode == ParameterMode.MetallicRoughnessGrid)
            {
                for (var row = 0; row < 5; row++)
                {
                    for (var column = 0; column < 5; column++)
                    {
                        var target = FindRenderer("TGT_M" + row + "_R" + column);
                        if (target != null) targets.Add(target);
                    }
                }

                return targets;
            }

            foreach (var targetName in contract.TargetNames)
            {
                var renderer = FindRenderer(targetName);
                if (renderer != null) targets.Add(renderer);
            }

            return targets;
        }

        private Renderer FindRenderer(string objectName)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                var transforms = root.GetComponentsInChildren<Transform>(true);
                foreach (var transform in transforms)
                {
                    if (transform.name != objectName) continue;
                    var renderer = transform.GetComponent<Renderer>();
                    if (renderer != null) return renderer;
                }
            }

            return null;
        }

        private static int GetExpectedTargetCount(SceneContract contract)
        {
            return contract.ParameterMode == ParameterMode.MetallicRoughnessGrid ? 25 : contract.TargetNames.Length;
        }

        private static int GetChartIndex(string objectName)
        {
            if (objectName == "REF_Mirror") return 1;
            if (objectName == "REF_Rough") return 2;
            return 0;
        }

        private static int GetGridRow(string objectName)
        {
            return objectName[5] - '0';
        }

        private static int GetGridColumn(string objectName)
        {
            return objectName[8] - '0';
        }

        private static string GetRendererKey(Renderer renderer)
        {
            return renderer.gameObject.scene.path + "|" + GetHierarchyPath(renderer.transform);
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var names = new List<string>();
            var current = transform;
            while (current != null)
            {
                names.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", names.ToArray());
        }

        private bool IsSelectedSceneActive()
        {
            return currentContract != null && SceneManager.GetActiveScene().name == currentContract.SceneName;
        }

        private static bool HasActiveLookDevScene()
        {
            return FindContract(SceneManager.GetActiveScene().name) != null;
        }

        private static SceneContract FindContract(string sceneName)
        {
            foreach (var contract in Contracts)
            {
                if (contract.SceneName == sceneName) return contract;
            }

            return null;
        }

        private static int FindContractIndex(string sceneName)
        {
            for (var i = 0; i < Contracts.Length; i++)
            {
                if (Contracts[i].SceneName == sceneName) return i;
            }

            return -1;
        }

        private void ShowError(string message, string title)
        {
            EditorUtility.DisplayDialog(title, message, "确定");
            SetStatus(message, MessageType.Error);
        }

        private void SetStatus(string message, MessageType type)
        {
            statusMessage = message;
            statusType = type;
            Repaint();
        }

        private void DrawStatus()
        {
            if (!string.IsNullOrEmpty(statusMessage))
                EditorGUILayout.HelpBox(statusMessage, statusType);
        }
    }
}
