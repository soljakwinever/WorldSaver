using UnityEngine;
using UnityEngine.Tilemaps;

namespace Project.Scripts
{
    [CreateAssetMenu(fileName = "New Biome", menuName = "Biome Data", order = 0)]
    public sealed class BiomeData : ScriptableObject
    {
        public string biomeName;
        
        public float temperature;
        public float temperatureVariance;

        public float moisture;
        public float moistureVariance;
            
        public float height;
        public float heightVariance;
            
        public float heightOffset = 0f;
        public float heightMultiplier = 1f;
        public float erosionStrength = 1f;
        public float mountainStrength = 1f;
        public float valleyStrength = 1f;
        public float roughnessStrength = 1f;
            
        public float hillStrength = 0.08f;
        public float hillScale = 28f;

        public float bumpStrength = 0.03f;
        public float bumpScale = 9f;

        public float cliffChance = 0.15f;
        public float cliffStrength = 0.08f;
        public float cliffScale = 18f;

        public float lakeStrength = 0.25f;
        
        public float volcanoChance = 0f;
        public float craterChance = 0f;
        public float mesaChance = 0f;
            
        public float townChance = 0f;
            
        public Color groundColor = Color.green;
        public Color pathColor = Color.lightGoldenRodYellow;
        public Color waterColor = Color.blue;
        public Color cliffColor = Color.red;
        public Color beachColor = Color.sandyBrown;
        
        public TileBase overrideGroundTile;
        public TileBase overridePathTile;
        public TileBase overrideWaterTile;
        public TileBase overrideCliffTile;
        public TileBase overrideBeachTile;

    }
}