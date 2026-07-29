using UnityEngine;
using Project.Scripts.Interface;

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

        public static readonly BlackboardKey<Vector3> Destination =
            new BlackboardKey<Vector3>("Destination");

        public static readonly BlackboardKey<GameObject> Self =
            new BlackboardKey<GameObject>("Self");

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

            if (!blackboard.ContainsLocal(Destination))
            {
                Vector3 initialDestination =
                    owner != null ? owner.transform.position : Vector3.zero;
                blackboard.Set(Destination, initialDestination);
            }
        }
    }
}
