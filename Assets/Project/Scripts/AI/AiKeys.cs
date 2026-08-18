using UnityEngine;
using Project.Scripts.Interface;
using Project.Scripts.DataTypes;

namespace Project.Scripts.AI
{
    /// <summary>
    /// Shared keys registered on every behaviour-tree blackboard.
    /// </summary>
    public static class AiKeys
    {
        /// <summary>
        /// Serializable identifiers used by graph constants. Resolve these through
        /// <see cref="Resolve"/> to obtain the identity-based runtime key.
        /// </summary>
        public enum Key
        {
            Self,
            Target,
            Destination
        }

        public static readonly BlackboardKey<Transform> Target =
            new BlackboardKey<Transform>("Target");

        /// <summary>
        /// Allows a running behavior to retain an acquired target beyond the
        /// sensor's normal acquisition radius.
        /// </summary>
        public static readonly BlackboardKey<float> TargetRetentionDistance =
            new BlackboardKey<float>("Target Retention Distance");

        public static readonly BlackboardKey<Vector3> Destination =
            new BlackboardKey<Vector3>("Destination");

        public static readonly BlackboardKey<TargetTrackingMemory> TargetMemory =
            new BlackboardKey<TargetTrackingMemory>("Target Memory");

        public static readonly BlackboardKey<GameObject> Self =
            new BlackboardKey<GameObject>("Self");

        /// <summary>
        /// True only while an idle branch wrapped in TickIdle is active.
        /// Spawn lifecycle systems use this marker without depending on a
        /// specific idle action such as Wander or Wait.
        /// </summary>
        public static readonly BlackboardKey<bool> IsIdle =
            new BlackboardKey<bool>("Is Idle");

        public static readonly BlackboardKey<EnemySpawnRule> SpawnRule =
            new BlackboardKey<EnemySpawnRule>("Spawn Rule");

        public static readonly BlackboardKey<EnemyData> EnemyData =
            new BlackboardKey<EnemyData>("Enemy Data");

        public static readonly BlackboardKey<int> Attack =
            new BlackboardKey<int>("Attack");

        public static readonly BlackboardKey<float> MovementSpeed =
            new BlackboardKey<float>("Movement Speed");

        public static readonly BlackboardKey<float> SprintMultiplier =
            new BlackboardKey<float>("Sprint Multiplier");

        public static readonly BlackboardKey<float> Accuracy =
            new BlackboardKey<float>("Accuracy");


        public static readonly BlackboardKey<SkillData> ConditionalSkill =
            new BlackboardKey<SkillData>("Conditional Skill");

        public static readonly BlackboardKey<ProjectileData> ConditionalProjectile =
            new BlackboardKey<ProjectileData>("Conditional Projectile");

        public static readonly BlackboardKey<WeaponSwingAnimation> ConditionalWeaponSwing =
            new BlackboardKey<WeaponSwingAnimation>("Conditional Weapon Swing");

        public static readonly BlackboardKey<ConditionalEnemySkill> ConditionalAttack =
            new BlackboardKey<ConditionalEnemySkill>("Conditional Attack");

        public static readonly BlackboardKey<ITimeController> TimeController =
            new BlackboardKey<ITimeController>("Time Controller");

        public static readonly BlackboardKey<IPathFindingService> PathFindingService =
            new BlackboardKey<IPathFindingService>("Path Finding Service");

        public static readonly BlackboardKey<IPathFindingMap> PathFindingMap =
            new BlackboardKey<IPathFindingMap>("Path Finding Map");

        public static readonly BlackboardKey<IAttackService> AttackService =
            new BlackboardKey<IAttackService>("Attack Service");

        public static readonly BlackboardKey<IProjectileService> ProjectileService =
            new BlackboardKey<IProjectileService>("Projectile Service");

        public static object Resolve(Key key)
        {
            return key switch
            {
                Key.Self => Self,
                Key.Target => Target,
                Key.Destination => Destination,
                _ => throw new System.ArgumentOutOfRangeException(
                    nameof(key),
                    key,
                    null)
            };
        }

        /// <summary>
        /// Adds all built-in AI keys and their initial values to a blackboard.
        /// Existing values are preserved, allowing callers to preconfigure overrides.
        /// </summary>
        public static void RegisterDefaults(
            Blackboard blackboard,
            GameObject owner)
        {
            if (!blackboard.ContainsLocal(Self))
                blackboard.Set(Self, owner);

            if (!blackboard.ContainsLocal(Target))
                blackboard.Set<Transform>(Target, null);

            if (!blackboard.ContainsLocal(TargetRetentionDistance))
                blackboard.Set(TargetRetentionDistance, 0f);

            if (!blackboard.ContainsLocal(Destination))
            {
                Vector3 initialDestination =
                    owner != null ? owner.transform.position : Vector3.zero;
                blackboard.Set(Destination, initialDestination);
            }

            if (!blackboard.ContainsLocal(TargetMemory))
                blackboard.Set(TargetMemory, new TargetTrackingMemory());

            if (!blackboard.ContainsLocal(IsIdle))
                blackboard.Set(IsIdle, false);

            if (!blackboard.ContainsLocal(SpawnRule))
                blackboard.Set<EnemySpawnRule>(SpawnRule, null);

            if (!blackboard.ContainsLocal(MovementSpeed))
                blackboard.Set(MovementSpeed, 2f);

            if (!blackboard.ContainsLocal(SprintMultiplier))
                blackboard.Set(SprintMultiplier, 1.5f);

            if (!blackboard.ContainsLocal(Accuracy))
                blackboard.Set(Accuracy, 1f);
        }
    }
}
