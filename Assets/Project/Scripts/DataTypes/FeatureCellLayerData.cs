using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "Feature Cell Layer", menuName = "World Generation/Layers/Feature Cells")]
    public sealed class FeatureCellLayerData : LayerData
    {
        [Min(1)] public int cellSize = 256;
        [Range(0f, 1f)] public float chancePerCell = 0.15f;
        public FeatureData[] features = Array.Empty<FeatureData>();

        [Header("Procedural Roads")]
        [Tooltip("Connect generated features that are close to one another with deterministic A* roads.")]
        public bool generateRoads;
        [Min(8f)] public float maximumRoadDistance = 384f;
        [Min(1)] public int roadPathStep = 4;
        [Min(0.5f)] public float roadWidth = 2f;
        [Range(0f, 1f), Tooltip("How strongly road bends steer toward the next path segment. Zero keeps the raw A* polyline; higher values produce rounder bends.")]
        public float roadSteeringWeight = 0.65f;
        [Range(0, 4), Tooltip("Number of steering passes used to round A* corners.")]
        public int roadSteeringPasses = 2;
        [Min(0f), Tooltip("Maximum deterministic sideways displacement applied to a smoothed road.")]
        public float roadJitter = 1.25f;
        [Tooltip("Allow otherwise unconnected nearby features to create a short branch to an existing road.")]
        public bool generateRoadBranches = true;
        [Min(1f), Tooltip("Maximum distance from a feature connection point to an existing road for creating a branch.")]
        public float maximumRoadBranchDistance = 96f;
        [Min(0), Tooltip("Additional cells kept clear of props that cannot spawn on paths, measured outward from the painted road or street edge.")]
        public int entityClearanceFromPaths = 3;
        [Min(0f), Tooltip("How strongly A* avoids steep height changes.")]
        public float roadSlopeCost = 18f;
        [Min(0f), Tooltip("Additional A* cost for routing across water.")]
        public float roadWaterCost = 30f;
        [Tooltip("Optional road surface. When empty, the biome path tile is used.")]
        public TileData roadTile;
    }
}
