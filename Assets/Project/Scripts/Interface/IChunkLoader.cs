using UnityEngine;

namespace Project.Scripts.Interface
{
    public interface IChunkLoader
    {
        int LoadedChunks { get; }
        float CurrentAmbientTemperature { get; }
        Vector2Int WorldSpawnPosition { get; }
        bool ShouldLoadChunk(Vector2Int chunkPosition);
        void ReportSpawn(IChunk chunk);
        void SetPortalPreview(Vector2 worldPosition, bool enabled);
        void SetTransitSpacePinned(Vector2 worldPosition, bool enabled);
    }

    public interface IWaterTileQuery
    {
        bool IsWaterTile(Vector3Int worldCell);
    }
}
