using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    [CreateAssetMenu(
        fileName = "Repair Town Effect",
        menuName = "World/Towns/Effects/Repair Structures")]
    public sealed class RepairTownEffect : TownEffect
    {
        [SerializeField, Min(1)] private int healthPerTick = 1;
        private readonly List<PersistentHealth> _healthTargets = new();
        private readonly HashSet<PersistentHealth> _uniqueHealthTargets = new();
        private readonly List<ITownTileRepairSource> _tileSources = new();

        public override TownEffectExecution Apply(
            TownCore town,
            long elapsedTicks,
            float availableMana)
        {
            if (town == null || elapsedTicks <= 0)
                return default;

            CollectHealthTargets(town);
            TownTileRepairRegistry.CopySourcesTo(_tileSources);

            int maximumMissingHealth = 0;
            foreach (PersistentHealth health in _healthTargets)
            {
                maximumMissingHealth = Mathf.Max(
                    maximumMissingHealth,
                    health.MaxHealth - health.Health);
            }

            Vector2 center = town.Position;
            foreach (ITownTileRepairSource source in _tileSources)
            {
                if (source == null)
                    continue;

                maximumMissingHealth = Mathf.Max(
                    maximumMissingHealth,
                    source.GetMaximumMissingHealth(
                        center,
                        town.TownRadius));
            }

            if (maximumMissingHealth <= 0)
                return default;

            long requiredTicks =
                (maximumMissingHealth + (long)healthPerTick - 1L) /
                healthPerTick;
            long affordableTicks = ManaCostPerTick <= 0f
                ? elapsedTicks
                : Math.Min(
                    elapsedTicks,
                    (long)Math.Floor(availableMana / ManaCostPerTick));
            long activeTicks = Math.Min(requiredTicks, affordableTicks);
            if (activeTicks <= 0)
                return default;

            int healing = activeTicks > int.MaxValue / healthPerTick
                ? int.MaxValue
                : (int)activeTicks * healthPerTick;
            int repairedTargets = 0;

            foreach (PersistentHealth health in _healthTargets)
            {
                if (health == null || health.Health >= health.MaxHealth)
                    continue;

                health.Heal(healing);
                repairedTargets++;
            }

            foreach (ITownTileRepairSource source in _tileSources)
            {
                if (source != null)
                {
                    repairedTargets += source.RepairDamagedTiles(
                        center,
                        town.TownRadius,
                        healing);
                }
            }

            return repairedTargets > 0
                ? new TownEffectExecution(
                    activeTicks,
                    activeTicks * ManaCostPerTick)
                : default;
        }

        private void CollectHealthTargets(TownCore town)
        {
            _healthTargets.Clear();
            _uniqueHealthTargets.Clear();

            foreach (GameObject building in town.Buildings)
            {
                if (building == null)
                    continue;

                PersistentHealth[] healthComponents =
                    building.GetComponentsInChildren<PersistentHealth>();
                foreach (PersistentHealth health in healthComponents)
                {
                    // A destroyed entity is not resurrected by maintenance.
                    if (health != null &&
                        health.Health > 0 &&
                        health.Health < health.MaxHealth &&
                        _uniqueHealthTargets.Add(health))
                    {
                        _healthTargets.Add(health);
                    }
                }
            }
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            healthPerTick = Mathf.Max(1, healthPerTick);
        }
#endif
    }
}
