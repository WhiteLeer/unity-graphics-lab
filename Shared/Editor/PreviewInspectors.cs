using UnityEditor;
using UnityEngine;

[CanEditMultipleObjects]
[CustomEditor(typeof(ReferencePreviewToggleController))]
public sealed class ReferencePreviewToggleControllerEditor : Editor
{
    private SerializedProperty previewControllerProp;
    private SerializedProperty templateProfileProp;
    private SerializedProperty previewMaterialCountProp;
    private SerializedProperty toggleKeyProp;
    private SerializedProperty targetCameraProp;
    private SerializedProperty currentModeProp;

    private void OnEnable()
    {
        previewControllerProp = serializedObject.FindProperty("previewController");
        templateProfileProp = serializedObject.FindProperty("templateProfile");
        previewMaterialCountProp = serializedObject.FindProperty("previewMaterialCount");
        toggleKeyProp = serializedObject.FindProperty("toggleKey");
        targetCameraProp = serializedObject.FindProperty("targetCamera");
        currentModeProp = serializedObject.FindProperty("currentMode");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawHeader("场景入口", "这个组件负责把场景配置写进去，并处理挡位切换。");
        DrawSection("绑定", () =>
        {
            EditorGUILayout.PropertyField(templateProfileProp, new GUIContent("场景配置"));
            EditorGUILayout.PropertyField(previewControllerProp, new GUIContent("预览控制"));
            EditorGUILayout.PropertyField(targetCameraProp, new GUIContent("目标相机"));
        });

        DrawSection("挡位", () =>
        {
            EditorGUILayout.PropertyField(toggleKeyProp, new GUIContent("切换键"));
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(previewMaterialCountProp, new GUIContent("挡位数量"));
            }

            EditorGUILayout.PropertyField(currentModeProp, new GUIContent("当前挡位"));
        });

        serializedObject.ApplyModifiedProperties();
    }

    private static void DrawHeader(string title, string message)
    {
        EditorGUILayout.HelpBox(message, MessageType.Info);
        EditorGUILayout.Space(2f);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    private static void DrawSection(string title, System.Action drawBody)
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            drawBody?.Invoke();
        }
    }
}

[CanEditMultipleObjects]
[CustomEditor(typeof(VolumeMaterialPreviewController), true)]
public sealed class VolumeMaterialPreviewControllerEditor : Editor
{
    private SerializedProperty currentMaterialIndexProp;
    private SerializedProperty previewShapeProp;
    private SerializedProperty allowRuntimeShapeSwitchProp;
    private SerializedProperty cycleShapeKeyProp;
    private SerializedProperty interactionControllerProp;
    private SerializedProperty renderControllerProp;
    private SerializedProperty templateProfileProp;

    private SerializedProperty sourceLightProp;
    private SerializedProperty preferSubstanceAtlasProp;
    private SerializedProperty densityAtlasProp;
    private SerializedProperty atlasColumnsProp;
    private SerializedProperty atlasRowsProp;
    private SerializedProperty textureResolutionProp;
    private SerializedProperty regenerateTextureProp;
    private SerializedProperty carrierModeProp;
    private SerializedProperty radiusProp;
    private SerializedProperty edgeSoftnessProp;
    private SerializedProperty noiseStrengthProp;
    private SerializedProperty noiseFrequencyProp;
    private SerializedProperty allowMouseRotateProp;
    private SerializedProperty mouseButtonProp;
    private SerializedProperty rotateSpeedXProp;
    private SerializedProperty rotateSpeedYProp;
    private SerializedProperty pitchLimitsProp;
    private SerializedProperty rotationSmoothTimeProp;
    private SerializedProperty rotationInertiaDampingProp;
    private SerializedProperty previewCameraProp;
    private SerializedProperty allowScrollZoomProp;
    private SerializedProperty zoomSpeedProp;
    private SerializedProperty zoomDistanceLimitsProp;
    private SerializedProperty applyTransformRotationProp;
    private SerializedProperty previewStateInitializedProp;

    private bool showLegacyMigration;

    private void OnEnable()
    {
        currentMaterialIndexProp = serializedObject.FindProperty("currentMaterialIndex");
        previewShapeProp = serializedObject.FindProperty("previewShape");
        allowRuntimeShapeSwitchProp = serializedObject.FindProperty("allowRuntimeShapeSwitch");
        cycleShapeKeyProp = serializedObject.FindProperty("cycleShapeKey");
        interactionControllerProp = serializedObject.FindProperty("interactionController");
        renderControllerProp = serializedObject.FindProperty("renderController");
        templateProfileProp = serializedObject.FindProperty("templateProfile");

        sourceLightProp = serializedObject.FindProperty("sourceLight");
        preferSubstanceAtlasProp = serializedObject.FindProperty("preferSubstanceAtlas");
        densityAtlasProp = serializedObject.FindProperty("densityAtlas");
        atlasColumnsProp = serializedObject.FindProperty("atlasColumns");
        atlasRowsProp = serializedObject.FindProperty("atlasRows");
        textureResolutionProp = serializedObject.FindProperty("textureResolution");
        regenerateTextureProp = serializedObject.FindProperty("regenerateTexture");
        carrierModeProp = serializedObject.FindProperty("carrierMode");
        radiusProp = serializedObject.FindProperty("radius");
        edgeSoftnessProp = serializedObject.FindProperty("edgeSoftness");
        noiseStrengthProp = serializedObject.FindProperty("noiseStrength");
        noiseFrequencyProp = serializedObject.FindProperty("noiseFrequency");
        allowMouseRotateProp = serializedObject.FindProperty("allowMouseRotate");
        mouseButtonProp = serializedObject.FindProperty("mouseButton");
        rotateSpeedXProp = serializedObject.FindProperty("rotateSpeedX");
        rotateSpeedYProp = serializedObject.FindProperty("rotateSpeedY");
        pitchLimitsProp = serializedObject.FindProperty("pitchLimits");
        rotationSmoothTimeProp = serializedObject.FindProperty("rotationSmoothTime");
        rotationInertiaDampingProp = serializedObject.FindProperty("rotationInertiaDamping");
        previewCameraProp = serializedObject.FindProperty("previewCamera");
        allowScrollZoomProp = serializedObject.FindProperty("allowScrollZoom");
        zoomSpeedProp = serializedObject.FindProperty("zoomSpeed");
        zoomDistanceLimitsProp = serializedObject.FindProperty("zoomDistanceLimits");
        applyTransformRotationProp = serializedObject.FindProperty("applyTransformRotation");
        previewStateInitializedProp = serializedObject.FindProperty("previewStateInitialized");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawHeader("预览主体", "这个组件负责管理预览状态、外形和连接关系。");

        DrawSection("主要状态", () =>
        {
            EditorGUILayout.PropertyField(templateProfileProp, new GUIContent("场景配置"));
            EditorGUILayout.PropertyField(currentMaterialIndexProp, new GUIContent("当前挡位"));
            EditorGUILayout.PropertyField(previewShapeProp, new GUIContent("当前形状"));
            EditorGUILayout.PropertyField(allowRuntimeShapeSwitchProp, new GUIContent("允许运行时切形状"));
            EditorGUILayout.PropertyField(cycleShapeKeyProp, new GUIContent("切形状键"));
        });

        DrawSection("连接", () =>
        {
            EditorGUILayout.PropertyField(interactionControllerProp, new GUIContent("交互控制"));
            EditorGUILayout.PropertyField(renderControllerProp, new GUIContent("渲染控制"));
        });

        showLegacyMigration = EditorGUILayout.Foldout(showLegacyMigration, "旧数据兼容", true);
        if (showLegacyMigration)
        {
            EditorGUILayout.HelpBox("这些字段只用于兼容旧场景数据，正常情况下不用管。", MessageType.None);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(sourceLightProp, new GUIContent("光源"));
                EditorGUILayout.PropertyField(preferSubstanceAtlasProp, new GUIContent("优先用材质图集"));
                EditorGUILayout.PropertyField(densityAtlasProp, new GUIContent("密度图集"));
                EditorGUILayout.PropertyField(atlasColumnsProp, new GUIContent("图集列数"));
                EditorGUILayout.PropertyField(atlasRowsProp, new GUIContent("图集行数"));
                EditorGUILayout.PropertyField(textureResolutionProp, new GUIContent("纹理分辨率"));
                EditorGUILayout.PropertyField(regenerateTextureProp, new GUIContent("重新生成纹理"));
                EditorGUILayout.PropertyField(carrierModeProp, new GUIContent("载体模式"));
                EditorGUILayout.PropertyField(radiusProp, new GUIContent("半径"));
                EditorGUILayout.PropertyField(edgeSoftnessProp, new GUIContent("边缘软化"));
                EditorGUILayout.PropertyField(noiseStrengthProp, new GUIContent("噪声强度"));
                EditorGUILayout.PropertyField(noiseFrequencyProp, new GUIContent("噪声频率"));
                EditorGUILayout.PropertyField(allowMouseRotateProp, new GUIContent("允许鼠标旋转"));
                EditorGUILayout.PropertyField(mouseButtonProp, new GUIContent("鼠标按键"));
                EditorGUILayout.PropertyField(rotateSpeedXProp, new GUIContent("横向旋转速度"));
                EditorGUILayout.PropertyField(rotateSpeedYProp, new GUIContent("纵向旋转速度"));
                EditorGUILayout.PropertyField(pitchLimitsProp, new GUIContent("俯仰角限制"));
                EditorGUILayout.PropertyField(rotationSmoothTimeProp, new GUIContent("旋转平滑时间"));
                EditorGUILayout.PropertyField(rotationInertiaDampingProp, new GUIContent("旋转惯性衰减"));
                EditorGUILayout.PropertyField(previewCameraProp, new GUIContent("预览相机"));
                EditorGUILayout.PropertyField(allowScrollZoomProp, new GUIContent("允许滚轮缩放"));
                EditorGUILayout.PropertyField(zoomSpeedProp, new GUIContent("缩放速度"));
                EditorGUILayout.PropertyField(zoomDistanceLimitsProp, new GUIContent("缩放范围"));
                EditorGUILayout.PropertyField(applyTransformRotationProp, new GUIContent("把旋转写到物体"));
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.PropertyField(previewStateInitializedProp, new GUIContent("已初始化"));
                }
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    private static void DrawHeader(string title, string message)
    {
        EditorGUILayout.HelpBox(message, MessageType.Info);
        EditorGUILayout.Space(2f);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    private static void DrawSection(string title, System.Action drawBody)
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            drawBody?.Invoke();
        }
    }
}

[CanEditMultipleObjects]
[CustomEditor(typeof(PreviewInteractionController))]
public sealed class PreviewInteractionControllerEditor : Editor
{
    private SerializedProperty allowMouseRotateProp;
    private SerializedProperty mouseButtonProp;
    private SerializedProperty rotateSpeedXProp;
    private SerializedProperty rotateSpeedYProp;
    private SerializedProperty pitchLimitsProp;
    private SerializedProperty rotationSmoothTimeProp;
    private SerializedProperty rotationInertiaDampingProp;
    private SerializedProperty previewCameraProp;
    private SerializedProperty allowScrollZoomProp;
    private SerializedProperty zoomSpeedProp;
    private SerializedProperty zoomDistanceLimitsProp;
    private SerializedProperty applyTransformRotationProp;

    private void OnEnable()
    {
        allowMouseRotateProp = serializedObject.FindProperty("allowMouseRotate");
        mouseButtonProp = serializedObject.FindProperty("mouseButton");
        rotateSpeedXProp = serializedObject.FindProperty("rotateSpeedX");
        rotateSpeedYProp = serializedObject.FindProperty("rotateSpeedY");
        pitchLimitsProp = serializedObject.FindProperty("pitchLimits");
        rotationSmoothTimeProp = serializedObject.FindProperty("rotationSmoothTime");
        rotationInertiaDampingProp = serializedObject.FindProperty("rotationInertiaDamping");
        previewCameraProp = serializedObject.FindProperty("previewCamera");
        allowScrollZoomProp = serializedObject.FindProperty("allowScrollZoom");
        zoomSpeedProp = serializedObject.FindProperty("zoomSpeed");
        zoomDistanceLimitsProp = serializedObject.FindProperty("zoomDistanceLimits");
        applyTransformRotationProp = serializedObject.FindProperty("applyTransformRotation");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawHeader("只管交互", "这个组件只负责运行时旋转和缩放，不负责挡位、材质或 shader 数据。");

        DrawSection("旋转", () =>
        {
            EditorGUILayout.PropertyField(allowMouseRotateProp, new GUIContent("允许鼠标旋转"));
            EditorGUILayout.PropertyField(mouseButtonProp, new GUIContent("鼠标按键"));
            EditorGUILayout.PropertyField(rotateSpeedXProp, new GUIContent("横向旋转速度"));
            EditorGUILayout.PropertyField(rotateSpeedYProp, new GUIContent("纵向旋转速度"));
            EditorGUILayout.PropertyField(pitchLimitsProp, new GUIContent("俯仰角限制"));
            EditorGUILayout.PropertyField(rotationSmoothTimeProp, new GUIContent("旋转平滑时间"));
            EditorGUILayout.PropertyField(rotationInertiaDampingProp, new GUIContent("旋转惯性衰减"));
            EditorGUILayout.PropertyField(applyTransformRotationProp, new GUIContent("把旋转写到物体"));
        });

        DrawSection("缩放", () =>
        {
            EditorGUILayout.PropertyField(previewCameraProp, new GUIContent("预览相机"));
            EditorGUILayout.PropertyField(allowScrollZoomProp, new GUIContent("允许滚轮缩放"));
            EditorGUILayout.PropertyField(zoomSpeedProp, new GUIContent("缩放速度"));
            EditorGUILayout.PropertyField(zoomDistanceLimitsProp, new GUIContent("缩放范围"));
        });

        var controller = (PreviewInteractionController)target;
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("当前状态", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.FloatField("俯仰角", controller.Pitch);
            EditorGUILayout.FloatField("水平角", controller.Yaw);
            EditorGUILayout.FloatField("缩放距离", controller.GetPreviewZoomDistance());
        }

        serializedObject.ApplyModifiedProperties();
    }

    private static void DrawHeader(string title, string message)
    {
        EditorGUILayout.HelpBox(message, MessageType.Info);
        EditorGUILayout.Space(2f);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    private static void DrawSection(string title, System.Action drawBody)
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            drawBody?.Invoke();
        }
    }
}

[CanEditMultipleObjects]
[CustomEditor(typeof(VolumePreviewRenderController))]
public sealed class VolumePreviewRenderControllerEditor : Editor
{
    private SerializedProperty sourceLightProp;
    private SerializedProperty preferSubstanceAtlasProp;
    private SerializedProperty densityAtlasProp;
    private SerializedProperty atlasColumnsProp;
    private SerializedProperty atlasRowsProp;
    private SerializedProperty textureResolutionProp;
    private SerializedProperty regenerateTextureProp;
    private SerializedProperty carrierModeProp;
    private SerializedProperty fullscreenQuadModeProp;
    private SerializedProperty legacyQuadCarrierObjectProp;
    private SerializedProperty sphereCarrierObjectProp;
    private SerializedProperty cubeCarrierObjectProp;
    private SerializedProperty capsuleCarrierObjectProp;

    private void OnEnable()
    {
        sourceLightProp = serializedObject.FindProperty("sourceLight");
        preferSubstanceAtlasProp = serializedObject.FindProperty("preferSubstanceAtlas");
        densityAtlasProp = serializedObject.FindProperty("densityAtlas");
        atlasColumnsProp = serializedObject.FindProperty("atlasColumns");
        atlasRowsProp = serializedObject.FindProperty("atlasRows");
        textureResolutionProp = serializedObject.FindProperty("textureResolution");
        regenerateTextureProp = serializedObject.FindProperty("regenerateTexture");
        carrierModeProp = serializedObject.FindProperty("carrierMode");
        fullscreenQuadModeProp = serializedObject.FindProperty("fullscreenQuadMode");
        legacyQuadCarrierObjectProp = serializedObject.FindProperty("legacyQuadCarrierObject");
        sphereCarrierObjectProp = serializedObject.FindProperty("sphereCarrierObject");
        cubeCarrierObjectProp = serializedObject.FindProperty("cubeCarrierObject");
        capsuleCarrierObjectProp = serializedObject.FindProperty("capsuleCarrierObject");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawHeader("渲染绑定", "这个组件负责选中当前载体、绑定生成的密度图，并写入 shader 参数。");

        DrawSection("光源和图集", () =>
        {
            EditorGUILayout.PropertyField(sourceLightProp, new GUIContent("光源"));
            EditorGUILayout.PropertyField(preferSubstanceAtlasProp, new GUIContent("优先用材质图集"));
            EditorGUILayout.PropertyField(densityAtlasProp, new GUIContent("密度图集"));
            EditorGUILayout.PropertyField(atlasColumnsProp, new GUIContent("图集列数"));
            EditorGUILayout.PropertyField(atlasRowsProp, new GUIContent("图集行数"));
            EditorGUILayout.PropertyField(textureResolutionProp, new GUIContent("纹理分辨率"));
            EditorGUILayout.PropertyField(regenerateTextureProp, new GUIContent("重新生成纹理"));
        });

        DrawSection("载体", () =>
        {
            EditorGUILayout.PropertyField(carrierModeProp, new GUIContent("载体模式"));
            EditorGUILayout.PropertyField(fullscreenQuadModeProp, new GUIContent("全屏四边形模式"));
            EditorGUILayout.PropertyField(legacyQuadCarrierObjectProp, new GUIContent("旧四边形载体"));
            EditorGUILayout.PropertyField(sphereCarrierObjectProp, new GUIContent("球体"));
            EditorGUILayout.PropertyField(cubeCarrierObjectProp, new GUIContent("立方体"));
            EditorGUILayout.PropertyField(capsuleCarrierObjectProp, new GUIContent("胶囊体"));
        });

        serializedObject.ApplyModifiedProperties();
    }

    private static void DrawHeader(string title, string message)
    {
        EditorGUILayout.HelpBox(message, MessageType.Info);
        EditorGUILayout.Space(2f);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    private static void DrawSection(string title, System.Action drawBody)
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            drawBody?.Invoke();
        }
    }
}

[CanEditMultipleObjects]
[CustomEditor(typeof(VolumePreviewSceneProfile))]
public sealed class VolumePreviewSceneProfileEditor : Editor
{
    private SerializedProperty toggleKeyProp;
    private SerializedProperty cameraBackgroundColorProp;
    private SerializedProperty sceneCameraDefaultsProp;
    private SerializedProperty previewModesProp;
    private SerializedProperty allowMouseRotateProp;
    private SerializedProperty mouseButtonProp;
    private SerializedProperty rotateSpeedXProp;
    private SerializedProperty rotateSpeedYProp;
    private SerializedProperty pitchLimitsProp;
    private SerializedProperty rotationSmoothTimeProp;
    private SerializedProperty rotationInertiaDampingProp;
    private SerializedProperty allowScrollZoomProp;
    private SerializedProperty zoomSpeedProp;
    private SerializedProperty zoomDistanceLimitsProp;
    private SerializedProperty applyTransformRotationProp;
    private SerializedProperty allowRuntimeShapeSwitchProp;
    private SerializedProperty cycleShapeKeyProp;

    private void OnEnable()
    {
        toggleKeyProp = serializedObject.FindProperty("toggleKey");
        cameraBackgroundColorProp = serializedObject.FindProperty("cameraBackgroundColor");
        sceneCameraDefaultsProp = serializedObject.FindProperty("sceneCameraDefaults");
        previewModesProp = serializedObject.FindProperty("previewModes");
        allowMouseRotateProp = serializedObject.FindProperty("allowMouseRotate");
        mouseButtonProp = serializedObject.FindProperty("mouseButton");
        rotateSpeedXProp = serializedObject.FindProperty("rotateSpeedX");
        rotateSpeedYProp = serializedObject.FindProperty("rotateSpeedY");
        pitchLimitsProp = serializedObject.FindProperty("pitchLimits");
        rotationSmoothTimeProp = serializedObject.FindProperty("rotationSmoothTime");
        rotationInertiaDampingProp = serializedObject.FindProperty("rotationInertiaDamping");
        allowScrollZoomProp = serializedObject.FindProperty("allowScrollZoom");
        zoomSpeedProp = serializedObject.FindProperty("zoomSpeed");
        zoomDistanceLimitsProp = serializedObject.FindProperty("zoomDistanceLimits");
        applyTransformRotationProp = serializedObject.FindProperty("applyTransformRotation");
        allowRuntimeShapeSwitchProp = serializedObject.FindProperty("allowRuntimeShapeSwitch");
        cycleShapeKeyProp = serializedObject.FindProperty("cycleShapeKey");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawHeader("配置数据", "这个资源是共享默认值的来源。场景应该直接引用它，不要在场景里再抄一份。");

        DrawSection("场景外观", () =>
        {
            EditorGUILayout.PropertyField(toggleKeyProp, new GUIContent("切换键"));
            EditorGUILayout.PropertyField(cameraBackgroundColorProp, new GUIContent("相机背景色"));
            EditorGUILayout.PropertyField(sceneCameraDefaultsProp, new GUIContent("相机默认值"), true);
        });

        DrawSection("预览挡位", () =>
        {
            var modeCount = EditorGUILayout.IntField("挡位数量", previewModesProp.arraySize);
            if (modeCount != previewModesProp.arraySize)
            {
                previewModesProp.arraySize = Mathf.Max(0, modeCount);
            }

            for (var i = 0; i < previewModesProp.arraySize; i++)
            {
                var element = previewModesProp.GetArrayElementAtIndex(i);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var displayNameProp = element.FindPropertyRelative("displayName");
                    var materialProp = element.FindPropertyRelative("previewMaterial");
                    var meshProp = element.FindPropertyRelative("previewMesh");
                    var fullscreenProp = element.FindPropertyRelative("useFullscreenQuad");

                    EditorGUILayout.PropertyField(displayNameProp, new GUIContent($"挡位 {i} 名称"));
                    EditorGUILayout.PropertyField(materialProp, new GUIContent("预览材质"));
                    EditorGUILayout.PropertyField(meshProp, new GUIContent("预览网格"));
                    EditorGUILayout.PropertyField(fullscreenProp, new GUIContent("使用全屏四边形"));
                }
            }
        });

        DrawSection("交互默认值", () =>
        {
            EditorGUILayout.PropertyField(allowMouseRotateProp, new GUIContent("允许鼠标旋转"));
            EditorGUILayout.PropertyField(mouseButtonProp, new GUIContent("鼠标按键"));
            EditorGUILayout.PropertyField(rotateSpeedXProp, new GUIContent("横向旋转速度"));
            EditorGUILayout.PropertyField(rotateSpeedYProp, new GUIContent("纵向旋转速度"));
            EditorGUILayout.PropertyField(pitchLimitsProp, new GUIContent("俯仰角限制"));
            EditorGUILayout.PropertyField(rotationSmoothTimeProp, new GUIContent("旋转平滑时间"));
            EditorGUILayout.PropertyField(rotationInertiaDampingProp, new GUIContent("旋转惯性衰减"));
            EditorGUILayout.PropertyField(allowScrollZoomProp, new GUIContent("允许滚轮缩放"));
            EditorGUILayout.PropertyField(zoomSpeedProp, new GUIContent("缩放速度"));
            EditorGUILayout.PropertyField(zoomDistanceLimitsProp, new GUIContent("缩放范围"));
            EditorGUILayout.PropertyField(applyTransformRotationProp, new GUIContent("把旋转写到物体"));
            EditorGUILayout.PropertyField(allowRuntimeShapeSwitchProp, new GUIContent("允许运行时切形状"));
            EditorGUILayout.PropertyField(cycleShapeKeyProp, new GUIContent("切形状键"));
        });

        serializedObject.ApplyModifiedProperties();
    }

    private static void DrawHeader(string title, string message)
    {
        EditorGUILayout.HelpBox(message, MessageType.Info);
        EditorGUILayout.Space(2f);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    private static void DrawSection(string title, System.Action drawBody)
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            drawBody?.Invoke();
        }
    }
}
