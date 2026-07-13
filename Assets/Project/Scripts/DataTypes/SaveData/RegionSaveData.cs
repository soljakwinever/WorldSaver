using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.DataTypes.SaveData
{
    [Serializable]
    public sealed class RegionSaveData
    {
        public const ushort Version = 1;
        
        public ushort version = Version;
        
        public Vector2Int coordinate;
        
        public long lastSimulatedTick;
        public long nextScheduledTick;

        public List<RegionComponentRecord> components = new();
        public List<ChunkState> changedChunks = new();
        
        public bool HasPersistentChanges => components.Count > 0 || changedChunks.Count > 0 || nextScheduledTick > 0;

        public RuntimeRegion CreateRuntimeRegion()
        {
            RuntimeRegion region = new(coordinate, lastSimulatedTick)
            {
                NextScheduledTick = nextScheduledTick
            };

            foreach (RegionComponentRecord component in components)
            {
                region.SetComponent(component,
                    component.CreateSnapshot(), markDirty: false);
            }
        }
    }
    
    [Serializable]
    public sealed class RegionComponentRecord
    {
        public ushort typeId;
        public ushort version;
        public byte[] data;
        
        public bool isAtBaseline;
        
        public RegionComponentRecord CreateSnapshot()
        {
            return new RegionComponentRecord
            {
                typeId = typeId,
                version = version,
                data = data == null ? Array.Empty<byte>() : (byte[]) data.Clone(),
                isAtBaseline = isAtBaseline
            };
        }
    }
}