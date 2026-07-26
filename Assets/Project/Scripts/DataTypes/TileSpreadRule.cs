using System;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [Flags]
    public enum TileOriginFilter
    {
        Generated = 1 << 0,
        Changed = 1 << 1,
        Any = Generated | Changed
    }

    public enum TerrainFeatureRequirement
    {
        Ignore = 0,
        Required = 1,
        Forbidden = 2
    }

    [CreateAssetMenu(fileName = "New Tile Spread Rule", menuName = "World/Tile Spread Rule")]
    public sealed class TileSpreadRule : ScriptableObject
    {
        [Tooltip("The tile that causes this rule to spread.")]
        public TileData sourceTile;

        [Tooltip("The tile placed on a successful spread. If empty, the source tile is used.")]
        public TileData resultTile;

        [Tooltip("Only these tiles may be replaced by this rule.")]
        public TileData[] validTargets = Array.Empty<TileData>();

        public PersistentTileLayer layer = PersistentTileLayer.Ground;

        [Header("Tile Origin")]
        [Tooltip("Whether the spreading tile may be procedural/generated, changed by gameplay, or either.")]
        public TileOriginFilter allowedSourceOrigins = TileOriginFilter.Any;

        [Tooltip("Whether the tile being replaced may be procedural/generated, changed by gameplay, or either.")]
        public TileOriginFilter allowedTargetOrigins = TileOriginFilter.Any;

        [Header("Terrain Conditions")]
        [Tooltip("When enabled, the destination's generated TerrainSample must satisfy these conditions.")]
        public bool restrictByTerrain;

        public BiomeData[] allowedBiomes = Array.Empty<BiomeData>();

        [Range(0f, 1f)] public float minHeight;
        [Range(0f, 1f)] public float maxHeight = 1f;
        [Range(0f, 1f)] public float minMoisture;
        [Range(0f, 1f)] public float maxMoisture = 1f;
        [Range(0f, 1f)] public float minTemperature;
        [Range(0f, 1f)] public float maxTemperature = 1f;

        public TerrainFeatureRequirement water = TerrainFeatureRequirement.Ignore;
        public TerrainFeatureRequirement cliff = TerrainFeatureRequirement.Ignore;
        public TerrainFeatureRequirement road = TerrainFeatureRequirement.Ignore;
        public TerrainFeatureRequirement trail = TerrainFeatureRequirement.Ignore;

        [Header("Timing")]
        [Min(1)]
        [Tooltip("How often this rule is evaluated, in persistent world ticks.")]
        public long intervalTicks = 30;

        [Range(0f, 1f)]
        [Tooltip("Chance for each eligible neighbouring tile on every evaluation.")]
        public float chancePerTarget = 0.1f;

        [Tooltip("Also considers diagonally adjacent tiles.")]
        public bool includeDiagonals;

        public TileData Result => resultTile != null ? resultTile : sourceTile;

        public bool CanReplace(TileData tile)
        {
            if (tile == null || validTargets == null)
                return false;

            for (int i = 0; i < validTargets.Length; i++)
            {
                if (validTargets[i] == tile)
                    return true;
            }

            return false;
        }

        public bool AllowsOrigin(bool isChanged)
        {
            return AllowsOrigin(isChanged, allowedTargetOrigins);
        }

        public bool AllowsSourceOrigin(bool isChanged)
        {
            return AllowsOrigin(isChanged, allowedSourceOrigins);
        }

        public bool MatchesTerrain(TerrainSample sample)
        {
            if (!restrictByTerrain)
                return true;

            if (sample.height < minHeight || sample.height > maxHeight ||
                sample.moisture < minMoisture || sample.moisture > maxMoisture ||
                sample.temperature < minTemperature || sample.temperature > maxTemperature ||
                !MatchesFeature(sample.isWater, water) ||
                !MatchesFeature(sample.isCliff, cliff) ||
                !MatchesFeature(sample.isRoad, road) ||
                !MatchesFeature(sample.isTrail, trail))
            {
                return false;
            }

            if (allowedBiomes == null || allowedBiomes.Length == 0)
                return true;

            for (int i = 0; i < allowedBiomes.Length; i++)
            {
                if (allowedBiomes[i] == sample.biome)
                    return true;
            }

            return false;
        }

        private static bool AllowsOrigin(bool isChanged, TileOriginFilter filter)
        {
            TileOriginFilter origin = isChanged
                ? TileOriginFilter.Changed
                : TileOriginFilter.Generated;
            return (filter & origin) != 0;
        }

        private static bool MatchesFeature(
            bool value,
            TerrainFeatureRequirement requirement)
        {
            return requirement switch
            {
                TerrainFeatureRequirement.Required => value,
                TerrainFeatureRequirement.Forbidden => !value,
                _ => true
            };
        }

        private void OnValidate()
        {
            intervalTicks = Math.Max(1, intervalTicks);
            chancePerTarget = Mathf.Clamp01(chancePerTarget);
            minHeight = Mathf.Clamp01(minHeight);
            maxHeight = Mathf.Clamp(maxHeight, minHeight, 1f);
            minMoisture = Mathf.Clamp01(minMoisture);
            maxMoisture = Mathf.Clamp(maxMoisture, minMoisture, 1f);
            minTemperature = Mathf.Clamp01(minTemperature);
            maxTemperature = Mathf.Clamp(maxTemperature, minTemperature, 1f);
        }
    }
}
