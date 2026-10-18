using System.IO;
using UnityEditor;
using UnityEngine;
using DealEngine;

public static class DealSizeTableExporter
{
    [MenuItem("Tools/Deal Engine/Export Deal Size Table (CSV)")]
    public static void Export()
    {
        DealConfig config = ConfigLoader.Load(out string source);
        string csv = DealSizeTable.BuildCsv(config);
        string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "DealSizeTable.csv");
        File.WriteAllText(path, csv);
        Debug.Log("Deal size table written to: " + path);
        EditorUtility.RevealInFinder(path);
    }
}