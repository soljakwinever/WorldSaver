using UnityEngine;
using System.Threading;
using Project.Scripts.DataTypes;

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
        Awaitable<bool> PreparePlaneTransitionAsync(
            PlaneData destination,
            Vector3 landingPosition,
            CancellationToken cancellationToken);
        void CompletePlaneTransition();
    }

    public interface IWaterTileQuery
    {
        bool IsWaterTile(Vector3Int worldCell);
    }
}
