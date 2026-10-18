using System.IO;
using DealEngine;
using UnityEditor;
using UnityEngine;

public static class PlaceholderConfigWriter
{
    [MenuItem("Tools/Deal Engine/Write Placeholder Config JSON")]
    public static void Write()
    {
        string folder = "Assets/Game/Resources";
        Directory.CreateDirectory(folder);
        string path = folder + "/deal-config.json";

        if (File.Exists(path) &&
            !EditorUtility.DisplayDialog("Overwrite config?", path + " already exists. Replace it with the placeholder?", "Replace", "Cancel"))
            return;

        File.WriteAllText(path, JsonUtility.ToJson(PlaceholderConfig.Create(), true));
        AssetDatabase.Refresh();
        Debug.Log("Config written to " + path + ". Edit this file to change bags, bet levels, limits or the version.");
    }
}