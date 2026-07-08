using System;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

namespace Project.Scripts
{
    [CreateAssetMenu(fileName = "World Data", menuName = "WorldData", order = 0)]
    public class WorldData : ScriptableObject
    {
        public TileBase[] tiles;

        public bool heightMapDebug = false;
        
        public float waterHeight = 0.2f;
        public float beachHeight = 0.3f;
        public float mountainHeight = 0.5f;
        
        public float cliffHeight = 0.005f;
        
        public float outCropStrength = 2f;
        public float outCropThreshold = 0.25f;
        public int outCropNoiseScale = 2;
        
        [Header("Noise Scales")]
        public float continentalNoiseScale = 46.5f;
        public float moistureNoiseScale = 32f;
        public float temperatureNoiseScale = 48.2f;
        public float erosionNoiseScale = 0.5f;
        public float valleyNoiseScale = 32;
        public float peakValleyNoiseScale = 8.5f;

        [Header("Valley")]
        public float valleyDepth = 0.22f;
        public float valleyWidth = 0.12f;
    
        [Header("Features")]
        public int featureCellSize = 256;
        public float featureChancePerCell = 0.85f;

        public float minFeatureRadius = 64f;
        public float maxFeatureRadius = 256f;
        
        public PropSpawnRule[] propSpawnRules;


        [Serializable]
        public class PropSpawnRule
        {
            public string name;
            
            public NodeData nodeData;
            
            [Header("Biome")]
            public string[] allowedBiomes;

            [Header("Terrain")] 
            public float minHeight = 0.35f;
            public float maxHeight = 0.85f;
            
            public float minMoisture = 0f;
            public float maxMoisture = 1f;
            
            public float minTemperature = 0f;
            public float maxTemperature = 1f;

            public float maxSlope = 0.08f;

            [Header("Placement")] 
            public float density = 0.15f;
            public int cellSize = 4;
            public float minSpacing = 2f;
            
            [Header("Noise")]
            public float noiseScale = 32f;
            public float noiseThreshold = 0.45f;

            [Header("Restrictions")] 
            public bool avoidWater = true;
            public bool avoidCliffs = true;
            public bool avoidMountains = true;
            public bool avoidTowns = true;
            public bool avoidRivers = true;
            public bool avoidRoads = true;
            public bool avoidTrails = true;
            public bool avoidBeaches = true;
            public bool avoidGround = true;

            [Header("Transform Randomization")] 
            public float minScale = 0.85f;
            public float maxScale = 1.15f;
            public bool randomFlipX = true;
        }
        
        [Serializable]
        public class Biome
        {
            public string name;
            
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

            public float volcanoChance = 0f;
            public float craterChance = 0f;
            public float mesaChance = 0f;
            
            public float townChance = 0f;
            
            public Color groundColor = Color.green;
            public Color pathColor = Color.lightGoldenRodYellow;
            public Color waterColor = Color.blue;
            public Color cliffColor = Color.red;
            
            [FormerlySerializedAs("sandColor")] public Color beachColor = Color.sandyBrown;
        }
    }
}