using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "Cave Layout Layer", menuName = "World Generation/Layers/Cave Layout")]
    public sealed class CaveLayoutLayerData : LayerData
    {
        [Header("Cellular Cave Shape")]
        [Min(1)] public int cellularCellSize = 2;
        [Tooltip("Base wall chance before the biome's Hill Strength openness modifier is applied.")]
        [Range(0f, 1f)] public float initialWallChance = 0.47f;
        [Range(0, 8)] public int cellularIterations = 5;
        [Range(0, 8)] public int wallBirthLimit = 5;
        [Range(0, 8)] public int wallSurvivalLimit = 4;

        [Header("Large Chamber Bias")]
        [Tooltip("Fallback chamber scale when the selected biome has no valid Hill Scale. Cave biomes normally use their existing Hill Scale field.")]
        [Min(1f)] public float chamberScale = 72f;
        [Range(0f, 0.5f)] public float chamberInfluence = 0.16f;
        [Min(1f)] public float warpScale = 96f;
        [Min(0f)] public float warpStrength = 24f;

        [Header("Connected Corridors")]
        [Min(32)] public int regionSize = 256;
        [Min(1f)] public float corridorHalfWidth = 5f;
        [Min(0f)] public float protectedRouteMargin = 4f;

        [Header("Crevasses")]
        [Min(4f)] public float crevasseCellSize = 42f;
        [Min(0.01f)] public float crevasseWidth = 0.075f;
        [Range(0f, 1f)] public float crevasseDensity = 0.55f;

        [Header("Output")]
        [Range(0f, 1f)] public float floorHeight = 0.45f;
        [Range(0f, 1f)] public float wallHeight = 0.85f;
        [Range(0f, 1f)] public float crevasseHeight = 0.05f;
        public TileData wallTile;
        public TileData crevasseTile;
    }
}
