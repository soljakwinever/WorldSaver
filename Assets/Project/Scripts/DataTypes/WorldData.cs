using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.Enums;
using UnityEngine;
using UnityEngine.Serialization;

namespace Project.Scripts
{
    [CreateAssetMenu(fileName = "World Data", menuName = "WorldData", order = 0)]
    public class WorldData : ScriptableObject
    {
        public TileData[] tiles;

        [Header("Tile Actions")]
        [Tooltip("Tile placed after mining. Individual biomes may override this value.")]
        public TileData minedTileReplacement;

        [Header("Tile Spreading")]
        [Tooltip("Rules such as grass spreading onto dirt. Only loaded chunks are simulated.")]
        public TileSpreadRule[] tileSpreadRules = Array.Empty<TileSpreadRule>();

        [Header("Tile Coverage")]
        [Tooltip("Coverage layers simulated on every non-water tile.")]
        public CoverageData[] coverageLayers = Array.Empty<CoverageData>();

        public bool TryGetTileData(int tileId, out TileData tileData)
        {
            tileData = null;
            if (tileId < 0)
                return false;

            if (tiles != null)
            {
                foreach (TileData candidate in tiles)
                {
                    if (candidate == null ||
                        candidate.TileId != tileId ||
                        !candidate.HasVisual)
                        continue;

                    if (tileData != null)
                    {
                        tileData = null;
                        return false;
                    }

                    tileData = candidate;
                }
            }

            return tileData != null;
        }

        private void OnValidate()
        {
            if (tiles == null)
                return;

            HashSet<int> ids = new();
            foreach (TileData tile in tiles)
            {
                if (tile == null)
                    continue;
                if (tile.TileId < 0 || !tile.HasVisual)
                    Debug.LogError($"Invalid tile definition '{tile.name}'.", tile);
                else if (!ids.Add(tile.TileId))
                    Debug.LogError($"Duplicate persistent tile ID {tile.TileId}.", this);
            }
        }

        public int seed;
        
        public bool heightMapDebug = false;
        public NoiseLayer previewNoiseLayer = NoiseLayer.Height;
        
        public float waterHeight = 0.2f;
        public float beachHeight = 0.3f;
        public float mountainHeight = 0.5f;

        [Header("Climate")]
        [Range(-2f, 2f)]
        [Tooltip("Initial normalized temperature offset applied to every weather region.")]
        public float globalTemperatureOffset;
        
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

        [Header("Grass Height")]
        [Min(0.01f)]
        [Tooltip("World-space scale of grass-height patches.")]
        public float grassHeightNoiseScale = 10f;

        [Header("Valley")]
        public float valleyDepth = 0.22f;
        public float valleyWidth = 0.12f;
        
        [Header("Lakes")]
        public float lakeDepth = 0.1f;
        public float lakeNoiseScale = 4;

        [Header("Small Pools")]
        [Min(0.01f)]
        public float SmallPoolsScale = 7f;
        [Min(0f)]
        public float SmallPoolsStrength = 0.06f;
        
        public float hillStrength = 2f;
        public float bumpStrength = 1f;
    
        [Header("Features")]
        public int featureCellSize = 256;
        public float featureChancePerCell = 0.85f;

        public float minFeatureRadius = 64f;
        public float maxFeatureRadius = 256f;
        
        public PropSpawnRule[] propSpawnRules;

        [Header("NPC Spawning")]
        public EnemySpawnRule[] enemySpawnRules = Array.Empty<EnemySpawnRule>();

        [Header("Runtime Entities")]
        public DataTypes.EntityArchetype[] runtimeEntityArchetypes;
        
        [Header("Time Data")]
        public int daysInMonth = 30;
        public float minutesPerDay = 24;
        public int startHour = 8;
        public Season startSeason = Season.Spring;

        [Header("Seasonal Biome Tint")]
        public Color springTint = Color.white;
        public Color summerTint = Color.white;
        public Color fallTint = Color.white;
        public Color winterTint = Color.white;

        [Tooltip("Controls how quickly biome colors transition from the previous season's tint to the current season's tint.")]
        public AnimationCurve seasonalTintCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [GradientUsage(true)]
        public Gradient dayColorGradient;
        
        public PlayerSettings playerSettings;
        
        public enum NoiseLayer
        {
            Temperature,
            Moisture,
            Erosion,
            Height,
            Roughness,
            Cliff,
            Hill,
            Bump,
            Volcano,
            Crater,
            PeakValley,
            MountainIsland,
            LocalLandforms,
            Lakes,
            GrassHeight,
            SmallPools
        }

        [Serializable]
        public class PlayerSettings
        {
            public float hungerRate = 0.005f;
            public float energyRate = 0.005f;
            
            public float movementEnergyMulpiplier = 1.1f;
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
