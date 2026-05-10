using UnityEditor;

public sealed class JadeVolumeShaderGUI : ShaderGUI
{
    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        DrawGroup(materialEditor, properties, "玉石颜色", "_BaseColor", "_AmbientTint", "_SkyTint");
        DrawGroup(materialEditor, properties, "形体和噪声", "_ShapeMode", "_ShapeBlend", "_NoiseFrequency", "_NoiseAmount", "_SurfaceOffset");
        DrawGroup(materialEditor, properties, "光照", "_FresnelPower", "_SpecularMultiplier", "_SpecularRoughness");
        DrawGroup(materialEditor, properties, "玉石通透", "_ScatterStrength", "_ScatterDistance", "_ScatterStep", "_ScatterIor", "_ScatterBlend", "_ScatterBoost", "_ScatterCurve");
        DrawGroup(materialEditor, properties, "性能", "_TraceSteps", "_HitEpsilon", "_MaxDistance", "_NormalStep");
    }

    private static void DrawGroup(MaterialEditor materialEditor, MaterialProperty[] properties, string title, params string[] propertyNames)
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

        using (new EditorGUI.IndentLevelScope())
        {
            foreach (var propertyName in propertyNames)
            {
                var property = FindProperty(propertyName, properties, false);
                if (property == null)
                {
                    continue;
                }

                materialEditor.ShaderProperty(property, property.displayName);
            }
        }
    }
}
