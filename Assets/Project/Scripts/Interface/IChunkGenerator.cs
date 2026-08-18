using System.Threading;
using UnityEngine;

namespace Project.Scripts.Interface
{
    public interface IChunkGenerator
    {
        public Awaitable Run(IChunkLoader loader, CancellationToken cancellationToken);
        public bool IsRunning { get; }
        int PendingWorkCount { get; }
        
        void RequestChunk(Vector2Int position);
        void ChunkUnloaded(Vector2Int position);
        Awaitable SuspendAndClearAsync(CancellationToken cancellationToken);
        void Resume();
    }
}
