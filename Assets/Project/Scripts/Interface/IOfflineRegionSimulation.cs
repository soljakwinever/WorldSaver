using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;

namespace Project.Scripts.Interface
{
    public interface IOfflineRegionSimulation
    {
        long GetNextUpdateTick(
            long currentTick,
            OfflineSimulationPolicy policy);

        void Simulate(
            RuntimeRegion region,
            long fromTick,
            long toTick,
            OfflineSimulationPolicy policy);
    }
}
