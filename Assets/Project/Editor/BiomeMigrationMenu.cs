#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;

public static class BiomeMigrationMenu
{
    [MenuItem("Tools/Biomes/Generate BiomeData Assets")]
    private static void GenerateBiomeDataAssets()
    {
        BiomeDataAssetGenerator.CreateBiomeAssets(
            BiomeLibrary.Biomes,
            "Assets/Data/Biomes",
            overwriteExisting: true
        );
    }
}

#endif