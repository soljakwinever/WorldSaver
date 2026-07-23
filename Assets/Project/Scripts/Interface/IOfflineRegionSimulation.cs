using Project.Scripts.DataTypes.SaveData;

namespace Project.Scripts.Interface
{
    public interface IOfflineRegionSimulation
    {
        long GetNextUpdateTick(
            long currentTick,
            OfflineSimulationPolicy policy);

        void Simulate(
            long fromTick,
            long toTick,
            OfflineSimulationPolicy policy);
    }
}