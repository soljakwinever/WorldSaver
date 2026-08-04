using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Interface
{
    public interface IWorldGenerator
    {
        public uint Seed { get; }
        public Vector2Int WorldSpawnPosition { get; }

        public int GetTile(int x, int y, out BiomeBlend biomeData, out float height, out float moisture,
            out float temperature);
        public TerrainSample GetTerrainSample(int x, int y);

        public Vector2Int FindSafeSpawnPosition(
            int searchRadius = 512,
            int maxAttempts = 5000,
            int safetyRadius = 3,
            float minHeight = 0.075f,
            float maxHeight = 0.7f);

        public bool TryFindSafePortalPosition(
            PlaneData destinationPlane,
            Vector2Int requestedCell,
            out Vector2Int safeCell,
            int searchRadius = 64,
            int clearanceRadius = 2);

        public ChunkBuildResult.IsCliff IsSmallCliff(int x, int y);
    }
}
