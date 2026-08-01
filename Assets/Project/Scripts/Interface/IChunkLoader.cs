using UnityEngine;

namespace Project.Scripts.Interface
{
    public interface IChunkLoader
    {
        int LoadedChunks { get; }
        float CurrentAmbientTemperature { get; }
        Vector2Int WorldSpawnPosition { get; }
        void ReportSpawn(IChunk chunk);
        void SetPortalPreview(Vector2 worldPosition, bool enabled);
    }
}
