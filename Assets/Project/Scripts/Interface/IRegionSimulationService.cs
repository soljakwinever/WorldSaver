using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;

namespace Project.Scripts.Interface
{
    public interface IRegionSimulationService
    {
        Awaitable<bool> SimulateAsync(
            RuntimeRegion region,
            long currentTick,
            OfflineSimulationPolicy policy);
    }
}
