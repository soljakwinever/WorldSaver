using UnityEngine;

namespace Project.Scripts.Interface
{
    public interface IChunk
    {
        public const int ChunkSize = 32;
        
        Vector2Int Position { get; set; }
    }
}