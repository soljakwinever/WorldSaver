using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using UnityEngine;

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

        public async Awaitable<bool> SimulateAsync(
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
            ulong expectedRevision = region.Revision;
            RuntimeRegion detached =
                RegionSaveData.CreateSnapshot(region).CreateRuntimeRegion();
            long nextTick = long.MaxValue;
            List<IRegionSimulationWork> backgroundWork = new();

            foreach (IOfflineRegionSimulation simulation in _simulations)
            {
                if (simulation is IPreparedRegionSimulation prepared)
                {
                    IRegionSimulationWork work = prepared.Prepare(
                        detached,
                        fromTick,
                        currentTick,
                        policy);
                    if (work != null)
                        backgroundWork.Add(work);
                }
                else
                {
                    // Legacy simulations remain main-thread-only until they
                    // explicitly provide a prepared Unity-free work item.
                    simulation.Simulate(
                        detached,
                        fromTick,
                        currentTick,
                        policy);
                }

                long candidate =
                    simulation.GetNextUpdateTick(currentTick, policy);

                if (candidate <= currentTick)
                    candidate = checked(currentTick + 1);

                nextTick = Math.Min(nextTick, candidate);
            }

            if (backgroundWork.Count > 0)
            {
                await Awaitable.BackgroundThreadAsync();
                try
                {
                    foreach (IRegionSimulationWork work in backgroundWork)
                        work.Execute();
                }
                finally
                {
                    await Awaitable.MainThreadAsync();
                }
            }

            detached.LastSimulatedTick = currentTick;
            detached.NextScheduledTick = nextTick;

            if (!region.TryApplySimulationResult(
                    detached,
                    expectedRevision))
            {
                return false;
            }

            foreach (IOfflineRegionSimulation simulation in _simulations)
            {
                if (simulation is IRegionSimulationAppliedHandler handler)
                    handler.OnRegionSimulationApplied(region);
            }

            return true;
        }
    }
}
