using System.Collections.Generic;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    public sealed class RuntimeRegion
    {
        private readonly Dictionary<ushort, ChunkState> _chunkStates = new();
        private readonly Dictionary<ushort, RegionComponentRecord> _components = new();

        private ulong _revision;
        private ulong _savedRevision;

        public Vector2Int Position { get; }
        public long LastSimulatedTick { get; set; }
        public long NextScheduledTick { get; set; }
        public ulong Revision => _revision;
        public bool IsDirty => _revision != _savedRevision;

        public IReadOnlyDictionary<ushort, ChunkState> ChangedChunks => _chunkStates;
        public IReadOnlyDictionary<ushort, RegionComponentRecord> Components => _components;

        public RuntimeRegion(Vector2Int position, long lastSimulatedTick)
        {
            Position = position;
            LastSimulatedTick = lastSimulatedTick;
        }

        public bool TryGetChunkState(
            ushort localChunkIndex,
            out ChunkState chunkState)
        {
            return _chunkStates.TryGetValue(localChunkIndex, out chunkState);
        }

        public void SetChunkState(
            ushort localChunkIndex,
            ChunkState chunkState,
            bool markDirty = true)
        {
            _chunkStates[localChunkIndex] = chunkState;

            if (markDirty)
                MarkDirty();
        }

        public bool RemoveChunkState(
            ushort localChunkIndex,
            bool markDirty = true)
        {
            bool removed = _chunkStates.Remove(localChunkIndex);

            if (removed && markDirty)
                MarkDirty();

            return removed;
        }

        public void SetComponent(
            RegionComponentRecord component,
            bool markDirty = true)
        {
            _components[component.typeId] = component;

            if (markDirty)
                MarkDirty();
        }

        public void MarkDirty()
        {
            checked
            {
                _revision++;
            }
        }

        // Only acknowledges the exact revision that was written. If the region
        // changed while the save was in flight, it remains dirty.
        public void MarkSaved(ulong writtenRevision)
        {
            if (_revision == writtenRevision)
                _savedRevision = writtenRevision;
        }
    }
}
