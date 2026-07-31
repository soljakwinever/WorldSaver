using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Bus
{
    public delegate void ChunkBuiltHandler(ChunkBuildResult result);
    
    public delegate void ChunkLoadedHandler(Vector2Int position, IChunk loaded);
    public delegate void ChunkUnloadedHandler(Vector2Int position);
    public delegate void NavigationCellChangedHandler(Vector2Int worldCell);
    public delegate void NavigationChunkChangedHandler(Vector2Int chunkPosition);
    public class MapSignalBus
    {
        public event ChunkBuiltHandler ChunkBuilt;
        public event ChunkLoadedHandler ChunkLoaded;
        public event ChunkUnloadedHandler ChunkUnloaded;
        public event NavigationCellChangedHandler NavigationCellChanged;
        public event NavigationChunkChangedHandler NavigationChunkChanged;
        
        public void RaiseChunkBuilt(ChunkBuildResult result)
        {
            ChunkBuilt?.Invoke(result);
        }
        
        public void RaiseChunkLoaded(Vector2Int position, IChunk loaded)
        {
            ChunkLoaded?.Invoke(position, loaded);
        }
        
        public void RaiseChunkUnloaded(Vector2Int position)
        {
            ChunkUnloaded?.Invoke(position);
        }

        public void RaiseNavigationCellChanged(Vector2Int worldCell)
        {
            NavigationCellChanged?.Invoke(worldCell);
        }

        public void RaiseNavigationChunkChanged(Vector2Int chunkPosition)
        {
            NavigationChunkChanged?.Invoke(chunkPosition);
        }
    }
}
