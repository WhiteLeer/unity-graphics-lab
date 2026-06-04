using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class EnsureTestDoubleSidedMaterials
{
    static EnsureTestDoubleSidedMaterials()
    {
        EditorApplication.delayCall += CreateIfNeeded;
    }

    private static void CreateIfNeeded()
    {
        const string whitePath = "Assets/White_DoubleSided.mat";
        const string blackPath = "Assets/Black_DoubleSided.mat";

        if (AssetDatabase.LoadAssetAtPath<Material>(whitePath) != null &&
            AssetDatabase.LoadAssetAtPath<Material>(blackPath) != null)
        {
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("HDRP/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null)
        {
            Debug.LogError("[EnsureTestDoubleSidedMaterials] Cannot find supported shader.");
            return;
        }

        CreateMaterialIfMissing(whitePath, shader, Color.white);
        CreateMaterialIfMissing(blackPath, shader, Color.black);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[EnsureTestDoubleSidedMaterials] Created White_DoubleSided.mat and Black_DoubleSided.mat");
    }

    private static void CreateMaterialIfMissing(string path, Shader shader, Color color)
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;

        Material mat = new Material(shader);
        mat.name = System.IO.Path.GetFileNameWithoutExtension(path);

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);

        if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", 0);
        if (mat.HasProperty("_CullMode")) mat.SetInt("_CullMode", 0);
        if (mat.HasProperty("_DoubleSidedEnable")) mat.SetFloat("_DoubleSidedEnable", 1f);

        AssetDatabase.CreateAsset(mat, path);
    }
}
