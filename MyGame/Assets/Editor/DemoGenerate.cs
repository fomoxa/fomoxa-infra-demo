using System.IO;
using UnityEditor;
using UnityEngine;

public static class DemoGenerate
{
    public static void Run()
    {
        EditorApplication.ExecuteMenuItem("Tools/Fomoxa/Generate");
        AssetDatabase.Refresh();

        const string folder = "Assets/Network/Generated";
        int count = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories).Length
            : 0;

        if (count == 0)
        {
            Debug.LogError("[Demo] generation produced no files in " + folder);
            EditorApplication.Exit(2);
            return;
        }

        foreach (string file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
        {
            Debug.Log("[Demo] generated " + file);
        }

        Debug.Log("[Demo] generated files: " + count);
        EditorApplication.Exit(0);
    }
}
