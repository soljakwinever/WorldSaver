using System.Collections.Generic;
using System.Threading.Tasks;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts
{
    public sealed class RegionRepository : IRegionRepository
    {
        private readonly Dictionary<Vector2Int, RuntimeRegion> _loadedRegions = new();
        private readonly Dictionary<Vector2Int, Task<RuntimeRegion>> _loadingRegions = new();
        private readonly HashSet<Vector2Int> _dirtyRegions = new();
        private readonly IRegionDiskStore _diskStore;

        public RegionRepository(IRegionDiskStore diskStore)
        {
            _diskStore = diskStore;
        }

        public async Awaitable<RuntimeRegion> GetReadyAsync(
            Vector2Int regionPosition,
            long currentTick)
        {
            if (_loadedRegions.TryGetValue(regionPosition, out RuntimeRegion loaded))
                return loaded;

            if (!_loadingRegions.TryGetValue(regionPosition, out Task<RuntimeRegion> loading))
            {
                loading = LoadRegionAsync(regionPosition, currentTick);
                _loadingRegions.Add(regionPosition, loading);
            }

            try
            {
                return await loading;
            }
            finally
            {
                _loadingRegions.Remove(regionPosition);
            }
        }

        public void MarkDirty(RuntimeRegion region)
        {
            if (!region.IsDirty)
                region.MarkDirty();

            _dirtyRegions.Add(region.Position);
        }

        public async Awaitable FlushDirtyAsync()
        {
            if (_dirtyRegions.Count == 0)
                return;

            Vector2Int[] positions = new Vector2Int[_dirtyRegions.Count];
            _dirtyRegions.CopyTo(positions);

            foreach (Vector2Int position in positions)
            {
                if (!_loadedRegions.TryGetValue(position, out RuntimeRegion region))
                {
                    _dirtyRegions.Remove(position);
                    continue;
                }

                ulong writtenRevision = region.Revision;
                RegionSaveData snapshot = RegionSaveData.CreateSnapshot(region);

                await _diskStore.SaveAsync(snapshot);

                region.MarkSaved(writtenRevision);

                if (!region.IsDirty)
                    _dirtyRegions.Remove(position);
            }
        }

        public async Awaitable ClearLoadedRegionsAsync()
        {
            while (_loadingRegions.Count != 0)
                await Awaitable.NextFrameAsync();
            if (_dirtyRegions.Count != 0)
            {
                throw new System.InvalidOperationException(
                    "Cannot change planes before dirty regions are flushed.");
            }

            _loadedRegions.Clear();
        }

        private async Task<RuntimeRegion> LoadRegionAsync(
            Vector2Int position,
            long currentTick)
        {
            RegionSaveData save = await _diskStore.LoadAsync(position);
            RuntimeRegion region = save?.CreateRuntimeRegion() ??
                                   new RuntimeRegion(position, currentTick);

            // GetReadyAsync callers resume on the Unity thread because the disk
            // store switches back before completing this task.
            if (_loadedRegions.TryGetValue(position, out RuntimeRegion existing))
                return existing;

            _loadedRegions.Add(position, region);

            // Insert IRegionSimulationService catch-up here later. It should be
            // the only layer that advances region-level simulation timestamps.
            return region;
        }
    }
}
