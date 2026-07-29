using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;

namespace Project.Scripts.Interface
{
    /// <summary>
    /// Creates a Unity-free work item on the main thread. Execute is invoked on
    /// a worker thread against a detached RuntimeRegion snapshot.
    /// </summary>
    public interface IPreparedRegionSimulation
    {
        IRegionSimulationWork Prepare(
            RuntimeRegion detachedRegion,
            long fromTick,
            long toTick,
            OfflineSimulationPolicy policy);
    }

    public interface IRegionSimulationWork
    {
        void Execute();
    }

    /// <summary>
    /// Optional main-thread notification after a detached result has passed
    /// revision validation and been applied to the live region.
    /// </summary>
    public interface IRegionSimulationAppliedHandler
    {
        void OnRegionSimulationApplied(RuntimeRegion region);
    }
}
