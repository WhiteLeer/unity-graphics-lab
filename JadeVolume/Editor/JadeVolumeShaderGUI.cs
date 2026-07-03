using UnityEditor;

public sealed class JadeVolumeShaderGUI : ShaderGUI
{
    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        var colorProperty = FindProperty("_BaseColor", properties, false);
        if (colorProperty != null)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("COLORVALUE", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                materialEditor.ShaderProperty(colorProperty, "COLORVALUE");
            }
        }

        EditorGUILayout.Space(8f);
        foreach (var property in properties)
        {
            if (property == null || property.name == "_BaseColor")
            {
                continue;
            }

            materialEditor.ShaderProperty(property, property.displayName);
        }
    }
}
