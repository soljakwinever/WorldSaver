using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;

namespace Project.Scripts.Interface
{
    public interface IRegionSimulationService
    {
        bool Simulate(
            RuntimeRegion region,
            long currentTick,
            OfflineSimulationPolicy policy);
    }
}
