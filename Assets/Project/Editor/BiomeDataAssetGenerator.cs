#if UNITY_EDITOR

using System.IO;
using System.Text.RegularExpressions;
using Project.Scripts;
using UnityEditor;
using UnityEngine;

// Adjust this namespace/import depending on where WorldData.Biome and BiomeData live.
// using Project.Scripts;

public static class BiomeDataAssetGenerator
{
    private const string DefaultOutputFolder = "Assets/Data/Biomes";

    /// <summary>
    /// Converts an array of old Biome objects into BiomeData asset files.
    /// </summary>
    public static void CreateBiomeAssets(
        WorldData.Biome[] biomes,
        string outputFolder = DefaultOutputFolder,
        bool overwriteExisting = true
    )
    {
        if (biomes == null || biomes.Length == 0)
        {
            Debug.LogWarning("No biomes were provided. The asset forge remains cold.");
            return;
        }

        EnsureFolderExists(outputFolder);

        int created = 0;
        int updated = 0;

        foreach (WorldData.Biome biome in biomes)
        {
            if (biome == null)
                continue;

            string safeName = MakeSafeFileName(biome.name);

            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "Unnamed Biome";

            string assetPath = $"{outputFolder}/{safeName}.asset";

            BiomeData asset = null;

            if (overwriteExisting)
            {
                asset = AssetDatabase.LoadAssetAtPath<BiomeData>(assetPath);
            }
            else
            {
                assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            }

            bool isNewAsset = asset == null;

            if (isNewAsset)
            {
                asset = ScriptableObject.CreateInstance<BiomeData>();
                AssetDatabase.CreateAsset(asset, assetPath);
                created++;
            }
            else
            {
                updated++;
            }

            CopyBiomeToBiomeData(biome, asset);

            EditorUtility.SetDirty(asset);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"BiomeData asset generation complete. Created: {created}, Updated: {updated}. " +
            $"Output: {outputFolder}"
        );
    }

    private static void CopyBiomeToBiomeData(WorldData.Biome source, BiomeData target)
    {
        target.biomeName = source.name;
        
        target.temperature = source.temperature;
        target.temperatureVariance = source.temperatureVariance;

        target.moisture = source.moisture;
        target.moistureVariance = source.moistureVariance;

        target.height = source.height;
        target.heightVariance = source.heightVariance;

        target.heightOffset = source.heightOffset;
        target.heightMultiplier = source.heightMultiplier;
        target.erosionStrength = source.erosionStrength;
        target.mountainStrength = source.mountainStrength;
        target.valleyStrength = source.valleyStrength;
        target.roughnessStrength = source.roughnessStrength;

        target.hillStrength = source.hillStrength;
        target.hillScale = source.hillScale;

        target.bumpStrength = source.bumpStrength;
        target.bumpScale = source.bumpScale;

        target.cliffChance = source.cliffChance;
        target.cliffStrength = source.cliffStrength;
        target.cliffScale = source.cliffScale;

        target.volcanoChance = source.volcanoChance;
        target.craterChance = source.craterChance;
        target.mesaChance = source.mesaChance;

        target.townChance = source.townChance;

        target.groundColor = source.groundColor;
        //target.dirtColor = source.dirtColor;
        target.pathColor = source.pathColor;
        target.waterColor = source.waterColor;
        target.cliffColor = source.cliffColor;
        target.beachColor = source.beachColor;
    }

    private static void EnsureFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string fullPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            folderPath
        );

        Directory.CreateDirectory(fullPath);
        AssetDatabase.Refresh();
    }

    private static string MakeSafeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "Unnamed Biome";

        string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
        string invalidPattern = $"[{invalidChars}]";

        string safeName = Regex.Replace(fileName, invalidPattern, "_");
        safeName = safeName.Trim();

        return safeName;
    }
}

#endif
