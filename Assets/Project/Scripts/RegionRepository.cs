using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts
{
    public sealed class RegionRepository : IRegionRepository
    {
        private readonly Dictionary<Vector2Int, RuntimeRegion> _loadedRegions = new();
        private readonly HashSet<Vector2Int> _dirtyRegions = new();

        private readonly IRegionDiskStore _diskStore;
        private readonly IRegionSimulationService _simulationService;
        
        public RegionRepository(IRegionDiskStore diskStore, IRegionSimulationService simulationService)
        {
            _diskStore = diskStore;
            _simulationService = simulationService;
        }

        public async Awaitable<RuntimeRegion> GetReadyAsync(Vector2Int regionPosition, long currentTick)
        {
            if (!_loadedRegions.TryGetValue(regionPosition, out RuntimeRegion region))
            {
                region = await _diskStore.LoadAsync(regionPosition) ?? new RuntimeRegion(regionPosition, currentTick);
                
                _loadedRegions.Add(regionPosition, region);
            }

            if (region.LastSimulatedTick < currentTick)
            {
                bool changed = _simulationService.CatchUp(region,region.LastSimulatedTick, currentTick);
                
                region.LastSimulatedTick = currentTick;
                
                if(changed)
                    MarkDirty(region);
            }
            
            return region;
        }

        public void MarkDirty(RuntimeRegion region)
        {
            region.MarkDirty();
            _dirtyRegions.Add(region.Position);
        }

        public async Awaitable FlushDirtyAsync()
        {
            if (_dirtyRegions.Count == 0) return;

            Vector2Int[] dirtyRegions = new Vector2Int[_dirtyRegions.Count];
            _dirtyRegions.CopyTo(dirtyRegions);

            foreach (Vector2Int regionPosition in dirtyRegions)
            {
                if (!_loadedRegions.TryGetValue(regionPosition, out RuntimeRegion region))
                {
                    continue;
                }
                
                /*
                 * Create a detached snapshot here.
                 * Do not ket background file operations inspect mutable runtime
                 * dictionaries direct.
                 */
                RegionSaveData snapshot =
                    RegionSaveData.CreateSnapshot(region);
                
                region.MarkSaved();
                _dirtyRegions.Remove(regionPosition);
            }
        }
    }
}