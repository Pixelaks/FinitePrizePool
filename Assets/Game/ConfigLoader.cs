using DealEngine;
using UnityEngine;

public static class ConfigLoader
{
    public const string ResourceName = "deal-config"; // Assets/Game/Resources/deal-config.json

    public static DealConfig Load(out string source)
    {
        TextAsset asset = Resources.Load<TextAsset>(ResourceName);
        if (asset != null)
        {
            DealConfig config = JsonUtility.FromJson<DealConfig>(asset.text);
            config.Validate();
            source = "Resources/deal-config.json";
            return config;
        }

        Debug.LogWarning("deal-config.json not found. Using the built-in placeholder config. " +
                         "Use Tools > Deal Engine > Write Placeholder Config JSON to create the file.");
        source = "built-in placeholder";
        return PlaceholderConfig.Create();
    }
}