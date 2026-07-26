using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;

namespace Project.Scripts
{
    public sealed class RegionSimulationService : IRegionSimulationService
    {
        private readonly List<IOfflineRegionSimulation> _simulations;

        public RegionSimulationService(
            List<IOfflineRegionSimulation> simulations)
        {
            _simulations = simulations;
        }

        public bool Simulate(
            RuntimeRegion region,
            long currentTick,
            OfflineSimulationPolicy policy)
        {
            if (policy == OfflineSimulationPolicy.None ||
                _simulations.Count == 0 ||
                currentTick <= region.LastSimulatedTick ||
                (policy == OfflineSimulationPolicy.Regional &&
                 region.NextScheduledTick > 0 &&
                 currentTick < region.NextScheduledTick))
            {
                return false;
            }

            long fromTick = region.LastSimulatedTick;
            long nextTick = long.MaxValue;

            foreach (IOfflineRegionSimulation simulation in _simulations)
            {
                simulation.Simulate(
                    region,
                    fromTick,
                    currentTick,
                    policy);

                long candidate =
                    simulation.GetNextUpdateTick(currentTick, policy);

                if (candidate <= currentTick)
                    candidate = checked(currentTick + 1);

                nextTick = Math.Min(nextTick, candidate);
            }

            region.LastSimulatedTick = currentTick;
            region.NextScheduledTick = nextTick;
            region.MarkDirty();
            return true;
        }
    }
}
