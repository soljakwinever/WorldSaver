using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.Entities;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts
{
    [DisallowMultipleComponent]
    public sealed class VillagerResourceDiscovery : MonoBehaviour
    {
        private TownCore _town;
        private TownJobBoard _board;

        private void Awake()
        {
            ResolveComponents();
        }

        public bool HasSource(ItemData item) => FindSource(item) != null;

        public bool TryIssueDemand(
            ItemData item,
            int requiredCount,
            TownJobPriority priority,
            string dependencyId,
            out string reason)
        {
            ResolveComponents();
            reason = string.Empty;
            if (item == null || requiredCount < 1 || _town == null ||
                _board == null || priority == TownJobPriority.Off)
            {
                reason = "The resource demand is invalid or gathering is disabled.";
                return false;
            }

            int available = _town.StockpileInventory.GetCount(item) +
                            CountResidentCargo(item) +
                            _board.GetActiveIncomingCount(item);
            if (available >= requiredCount)
                return true;
            if (!string.IsNullOrEmpty(dependencyId) &&
                _board.HasActiveConstructionJob(dependencyId))
                return true;

            HarvestableObject source = FindSource(item);
            if (source == null)
            {
                reason = $"No nearby harvestable source produces {item.name}.";
                return false;
            }

            var expected = new[]
            {
                new InventoryChange(
                    item,
                    source.MaximumOutput,
                    ItemData.Rarity.Common)
            };
            if (!_town.StockpileInventory.CanApplyChanges(expected))
            {
                reason = "The town stockpile has no room for the gathered resources.";
                return false;
            }

            UnityEngine.Object target = source.PersistentEntity is Component persistent
                ? persistent
                : source;
            if (_board.IsTargetInvalid(
                    VillagerJobType.Gather,
                    target,
                    source.transform.position) ||
                _board.HasActiveTarget(target, VillagerJobType.Gather))
            {
                reason = "The needed resource is already assigned or temporarily unreachable.";
                return false;
            }

            if (!_board.TryIssue(new TownJobRequest(
                    VillagerJobType.Gather,
                    source.transform.position,
                    1f,
                    priority,
                    target,
                    item,
                    null,
                    dependencyId), out _))
            {
                reason = "The town could not issue the gathering job.";
                return false;
            }
            return true;
        }

        public ItemData FindGatherableItem(EntityTag tag)
        {
            if (tag == null) return null;
            HarvestableObject best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (HarvestableObject source in
                     FindObjectsByType<HarvestableObject>(FindObjectsSortMode.None))
            {
                if (source?.OutputItem == null ||
                    !source.OutputItem.HasTag(tag) ||
                    !_town.ContainsResourcePosition(source.transform.position))
                    continue;
                float distance = (source.transform.position - _town.Position)
                    .sqrMagnitude;
                if (distance < bestDistance)
                {
                    best = source;
                    bestDistance = distance;
                }
            }
            return best?.OutputItem;
        }

        private HarvestableObject FindSource(ItemData item)
        {
            ResolveComponents();
            if (item == null || _town == null || _board == null) return null;
            HarvestableObject best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (HarvestableObject source in
                     FindObjectsByType<HarvestableObject>(FindObjectsSortMode.None))
            {
                if (source == null || source.OutputItem != item ||
                    !_town.ContainsResourcePosition(source.transform.position))
                    continue;
                UnityEngine.Object target = source.PersistentEntity is Component persistent
                    ? persistent
                    : source;
                if (_board.HasActiveTarget(target, VillagerJobType.Gather))
                    continue;
                float distance = (source.transform.position - _town.Position)
                    .sqrMagnitude;
                if (distance < bestDistance)
                {
                    best = source;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private int CountResidentCargo(ItemData item)
        {
            int count = 0;
            string townId = _town.PersistentEntity?.Id.ToString() ?? string.Empty;
            foreach (VillagerEntityBridge villager in VillagerEntityBridge.All)
                if (villager != null && villager.TownId == townId)
                    count += villager.GetCount(item);
            return count;
        }

        private void ResolveComponents()
        {
            _town ??= GetComponent<TownCore>();
            _board ??= GetComponent<TownJobBoard>();
        }
    }
}
