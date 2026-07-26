using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Pathfinding
{
    /// <summary>Navigation view of the deterministic generated world.</summary>
    public sealed class WorldPathFindingMap : IPathFindingMap
    {
        private readonly WorldGeneration _worldGeneration;

        public WorldPathFindingMap(WorldGeneration worldGeneration)
        {
            _worldGeneration = worldGeneration;
        }

        public bool IsWalkable(Vector2Int worldCell)
        {
            TerrainSample sample =
                _worldGeneration.GetTerrainSample(worldCell.x, worldCell.y);
            return !sample.isWater && !sample.isCliff;
        }

        public float GetTraversalCost(Vector2Int worldCell)
        {
            TerrainSample sample =
                _worldGeneration.GetTerrainSample(worldCell.x, worldCell.y);
            // Prefer roads and trails while retaining an admissible cost floor.
            return sample.isRoad ? 1f : sample.isTrail ? 1.1f : 1.25f;
        }
    }
}
