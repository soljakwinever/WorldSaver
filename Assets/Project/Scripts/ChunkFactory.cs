using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    public class ChunkFactory : IFactory<Transform, Vector2Int, Chunk>
    {
        DiContainer _container;
        
        [Inject] private Chunk _chunkPrefab;

        public ChunkFactory(DiContainer container)
        {
            _container = container;
        }
        
        public Chunk Create(Transform parent, Vector2Int position)
        {
            var chunkGameObject = _container.InstantiatePrefab(_chunkPrefab, parent);
            var chunk = chunkGameObject.GetComponent<Chunk>();
            chunk.Position = position;
            return chunk;
        }
    }
}