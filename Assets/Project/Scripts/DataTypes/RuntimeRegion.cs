using System.Collections.Generic;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    public sealed class RuntimeRegion
    {
        private readonly Dictionary<ushort, ChunkState> _chunkStates = new();
        
        public Vector2Int Position { get; }
        public long LastSimulatedTick { get; set; }
        public long NextScheduledTick { get; set; }
        
        public bool IsDirty { get; private set; }

        public RuntimeRegion(Vector2Int position, long lastSimulatedTick)
        {
            Position = position;
            LastSimulatedTick = lastSimulatedTick;
        }

        public bool TryGetChunkState(ushort localChunkIndex, out ChunkState chunkState)
        {
            return _chunkStates.TryGetValue(localChunkIndex, out chunkState);
        }
        
        public void SetChunkState(ushort localChunkIndex, ChunkState chunkState)
        {
            _chunkStates[localChunkIndex] = chunkState;
            IsDirty = true;
        }

        public void RemoveChunkState(ushort localChunkIndex)
        {
            if(_chunkStates.Remove(localChunkIndex))
                IsDirty = true;
        }
        
        public IReadOnlyDictionary<ushort, ChunkState> ChangedChunks => _chunkStates;

        public void MarkDirty()
        {
            IsDirty = true;
        }

        public void MarkSaved()
        {
            IsDirty = false;
        }

        public void SetComponent(RegionComponentRecord component, RegionComponentRecord createSnapshot, bool markDirty)
        {
            
        }
    }
}