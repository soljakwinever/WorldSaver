using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Interface
{
    public interface IRegionRepository
    {
        Awaitable<RuntimeRegion> GetReadyAsync(
            Vector2Int regionPosition,
            long currentTick);

        void MarkDirty(RuntimeRegion region);
        Awaitable FlushDirtyAsync();
    }
}
