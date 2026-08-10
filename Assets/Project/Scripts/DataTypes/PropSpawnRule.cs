using System;
using UnityEngine;

namespace Project.Scripts
{
    [Serializable]
    public class PropSpawnRule
    {
        public string name;
            
        public NodeData nodeData;
            
        [Header("Biome")]
        public BiomeData[] allowedBiomes;
        public string[] disallowedBiomes;

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
        [HideInInspector, Tooltip("Legacy setting retained for serialized compatibility. Use Can Spawn On Paths.")]
        public bool avoidRoads = true;
        [Tooltip("Allow this prop on generated roads and town streets. Overrides Avoid Roads for explicitly permitted entities.")]
        public bool canSpawnOnPaths;
        public bool avoidTrails = true;
        public bool avoidBeaches = true;
        public bool avoidGround = true;

        [Header("Transform Randomization")] 
        public float minScale = 0.85f;
        public float maxScale = 1.15f;
        public bool randomFlipX = true;
        
        public class Ruleset
        {
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
            [HideInInspector] public bool avoidRoads = true;
            public bool canSpawnOnPaths;
            public bool avoidTrails = true;
            public bool avoidBeaches = true;
            public bool avoidGround = true;
        }
    }
}
