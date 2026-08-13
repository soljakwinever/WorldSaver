using UnityEngine;

namespace Project.Scripts.Interface
{
    public interface IChunk
    {
        public const int ChunkSize = 32;

        void BeginInit(ChunkBuildResult result);
        ChunkInitializationStatus AdvanceInit(double deadline);
        void CancelInit();
        Vector2Int Position { get; set; }
    }

    public enum ChunkInitializationStatus
    {
        InProgress,
        Completed,
        Failed
    }
}
