using System;
using System.Collections.Generic;
using Project.Scripts.Core;
using Project.Scripts.Interface;
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
        private readonly Collider2D[] _entityResults = new Collider2D[128];

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
                    TryAddHealthTarget(health);
            }

            int entityCount = Physics2D.OverlapCircleNonAlloc(
                town.Position,
                town.TownRadius,
                _entityResults);
            for (int i = 0; i < entityCount; i++)
            {
                Collider2D collider = _entityResults[i];
                PersistentHealth health = collider != null
                    ? collider.GetComponentInParent<PersistentHealth>()
                    : null;
                if (health == null ||
                    !town.ContainsTownPosition(health.transform.position) ||
                    !BelongsToPlayerOrTown(health, town))
                    continue;

                TryAddHealthTarget(health);
            }
        }

        private void TryAddHealthTarget(PersistentHealth health)
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

        private static bool BelongsToPlayerOrTown(
            PersistentHealth health,
            TownCore town)
        {
            if (health.GetComponentInParent<PlayerDataController>() != null)
                return true;

            IPersistentEntity entity =
                health.GetComponentInParent<IPersistentEntity>();
            IPersistentEntity townEntity =
                town.GetComponentInParent<IPersistentEntity>();
            if (entity != null &&
                (entity == townEntity ||
                 IsTownResident(town, entity.Id.ToString())))
                return true;

            IAiPathingAgent pathingAgent =
                health.GetComponentInParent<IAiPathingAgent>();
            if (pathingAgent == null || townEntity == null)
                return false;

            return string.Equals(
                pathingAgent.CapturePathFindingQuery().VillageId,
                townEntity.Id.ToString(),
                StringComparison.Ordinal);
        }

        private static bool IsTownResident(TownCore town, string entityId)
        {
            foreach (string residentId in town.ResidentIds)
            {
                if (string.Equals(
                        residentId,
                        entityId,
                        StringComparison.Ordinal))
                    return true;
            }

            return false;
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
