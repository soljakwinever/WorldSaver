using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.Entities;
using UnityEngine;

namespace Project.Scripts.Interface
{
    public readonly struct TownJobRequest
    {
        public readonly VillagerJobType Type;
        public readonly TownJobPriority Priority;
        public readonly Vector3 Position;
        public readonly float WorkRequired;
        public readonly Object Target;
        public readonly ItemData Item;
        public readonly CraftingRecipeData Recipe;
        public readonly string ConstructionId;

        public TownJobRequest(
            VillagerJobType type,
            Vector3 position,
            float workRequired = 1f,
            TownJobPriority priority = TownJobPriority.Normal,
            Object target = null,
            ItemData item = null,
            CraftingRecipeData recipe = null,
            string constructionId = "")
        {
            Type = type;
            Priority = priority;
            Position = position;
            WorkRequired = workRequired;
            Target = target;
            Item = item;
            Recipe = recipe;
            ConstructionId = constructionId ?? string.Empty;
        }
    }

    public readonly struct TownJobView
    {
        public readonly long Id;
        public readonly VillagerJobType Type;
        public readonly TownJobPriority Priority;
        public readonly TownJobStatus Status;
        public readonly Vector3 Position;
        public readonly string BlockedReason;
        public readonly string ConstructionId;

        public TownJobView(long id, VillagerJobType type,
            TownJobPriority priority, TownJobStatus status,
            Vector3 position, string blockedReason,
            string constructionId = "")
        {
            Id = id;
            Type = type;
            Priority = priority;
            Status = status;
            Position = position;
            BlockedReason = blockedReason;
            ConstructionId = constructionId ?? string.Empty;
        }
    }

    public interface ITownJobBoard
    {
        IReadOnlyList<TownJobView> Jobs { get; }
        bool TryIssue(TownJobRequest request, out long jobId);
        bool TryCancel(long jobId);
        TownJobPriority GetPriority(VillagerJobType type);
        void SetPriority(VillagerJobType type, TownJobPriority priority);
        bool HasActiveConstructionJob(string constructionId);
        void CancelConstructionJobs(string constructionId);
    }

    public interface IEquipmentController
    {
        IReadOnlyList<IItemStack> EquippedItems { get; }
        bool TryEquip(IItemStack stack);
        bool TryUnequip(EquipmentSlot slot);
    }

    /// <summary>Marker allowing existing enemy sensors to include this NPC.</summary>
    public interface IEnemyTarget
    {
        GameObject TargetObject { get; }
    }

    public static class EnemyTargetRegistry
    {
        private static readonly HashSet<IEnemyTarget> Targets = new();
        public static IReadOnlyCollection<IEnemyTarget> All => Targets;
        public static void Register(IEnemyTarget target)
        { if (target != null) Targets.Add(target); }
        public static void Unregister(IEnemyTarget target)
        { if (target != null) Targets.Remove(target); }
    }

    public interface IVillagerWorldAdapter
    {
        bool TryBuild(ItemData buildingItem, Vector3 position, out string reason);
    }
}
