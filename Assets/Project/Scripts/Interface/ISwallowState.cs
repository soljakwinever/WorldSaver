namespace Project.Scripts.Interface
{
    using System.Collections.Generic;
    using Project.Scripts.DataTypes;

    public interface ITargetableState
    {
        bool CanBeTargeted { get; }
    }

    public interface IStunState
    {
        bool IsStunned { get; }
    }

    public interface ISwallowedState
    {
        bool IsSwallowed { get; }
        float Struggle01 { get; }
        bool TryStruggle();
    }

    public interface IEntityTagProvider
    {
        IReadOnlyList<EntityTag> EntityTags { get; }
    }

    public interface ISwallowOccupancyState
    {
        bool IsFull { get; }
    }

    public interface IOffscreenRetreatState
    {
        bool ShouldRetreatOffscreen { get; }
    }

    public interface IOffscreenDespawnHandler
    {
        void PrepareForOffscreenDespawn();
    }

    public interface IMovementSpeedMultiplier
    {
        float MovementSpeedMultiplier { get; }
    }

    public interface IEruptionPatternSpawner
    {
        void SpawnPattern(
            SkillActionContext context,
            EruptionPatternData pattern,
            UnityEngine.Vector3 completionPosition);
    }
}
