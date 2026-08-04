using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.DataTypes.SaveData
{
    [Serializable]
    public sealed class RegionSaveData
    {
        public const ushort CurrentVersion = 5;

        public ushort version = CurrentVersion;
        public Vector2Int coordinate;
        public long lastSimulatedTick;
        public long nextScheduledTick;
        public List<RegionComponentRecord> components = new();
        public List<ChunkState> changedChunks = new();

        public RuntimeRegion CreateRuntimeRegion()
        {
            RuntimeRegion region = new(coordinate, lastSimulatedTick)
            {
                NextScheduledTick = nextScheduledTick
            };

            if (components != null)
            {
                foreach (RegionComponentRecord component in components)
                {
                    if (component != null)
                        region.SetComponent(component.CreateSnapshot(), markDirty: false);
                }
            }

            if (changedChunks != null)
            {
                foreach (ChunkState chunk in changedChunks)
                {
                    if (chunk != null)
                    {
                        region.SetChunkState(
                            chunk.localChunkIndex,
                            chunk.CreateSnapshot(),
                            markDirty: false);
                    }
                }
            }

            return region;
        }

        public static RegionSaveData CreateSnapshot(RuntimeRegion region)
        {
            RegionSaveData snapshot = new()
            {
                coordinate = region.Position,
                lastSimulatedTick = region.LastSimulatedTick,
                nextScheduledTick = region.NextScheduledTick
            };

            foreach (RegionComponentRecord component in region.Components.Values)
                snapshot.components.Add(component.CreateSnapshot());

            foreach (ChunkState chunk in region.ChangedChunks.Values)
                snapshot.changedChunks.Add(chunk.CreateSnapshot());

            snapshot.components.Sort(static (left, right) =>
                left.typeId.CompareTo(right.typeId));
            snapshot.changedChunks.Sort(static (left, right) =>
                left.localChunkIndex.CompareTo(right.localChunkIndex));

            return snapshot;
        }
    }

    [Serializable]
    public sealed class RegionComponentRecord
    {
        public ushort typeId;
        public ushort version;
        public byte[] data = Array.Empty<byte>();
        public bool isAtBaseline;

        public RegionComponentRecord CreateSnapshot()
        {
            return new RegionComponentRecord
            {
                typeId = typeId,
                version = version,
                data = data == null ? Array.Empty<byte>() : (byte[])data.Clone(),
                isAtBaseline = isAtBaseline
            };
        }
    }
}
