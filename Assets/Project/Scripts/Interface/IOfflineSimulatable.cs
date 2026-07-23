using Project.Scripts.DataTypes.SaveData;

namespace Project.Scripts.Interface
{
    public interface IOfflineSimulatable
    {
        void SimulateOffline(
            long fromTick,
            long toTick,
            OfflineSimulationPolicy policy
        );
    }
}