using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(
        fileName = "Projectile",
        menuName = "Data/Projectile",
        order = 0)]
    public sealed class ProjectileData : ScriptableObject
    {
        [Tooltip("Prefab containing a Projectile component and a 2D collider.")]
        public GameObject prefab;

        [Min(0f)]
        public float speed = 8f;

        [Min(0.01f)]
        public float lifetime = 5f;

        [Tooltip("Only collisions with these layers are considered.")]
        public LayerMask collisionMask = ~0;

        [Tooltip("Despawn when hitting a matching-layer collider that is not damageable.")]
        public bool despawnOnEnvironmentHit = true;

        [Tooltip("Rotate the projectile's local right axis toward its travel direction.")]
        public bool rotateToDirection = true;

#if UNITY_EDITOR
        private void OnValidate()
        {
            speed = Mathf.Max(0f, speed);
            lifetime = Mathf.Max(0.01f, lifetime);
        }
#endif
    }

    /// <summary>
    /// Complete, owner-agnostic description of one projectile launch.
    /// AttackContext identifies the source and damage while direction controls
    /// physical travel.
    /// </summary>
    public readonly struct ProjectileLaunchContext
    {
        public ProjectileData Projectile { get; }
        public AttackContext Attack { get; }
        public Vector3 Origin { get; }
        public Vector2 Direction { get; }
        public Vector3? Destination { get; }
        public Action<Vector3> OnImpact { get; }
        public GameObject TravelEffectPrefab { get; }
        public bool DealDirectDamageOnImpact { get; }

        public ProjectileLaunchContext(
            ProjectileData projectile,
            AttackContext attack,
            Vector3 origin,
            Vector2 direction,
            Vector3? destination = null,
            Action<Vector3> onImpact = null,
            GameObject travelEffectPrefab = null,
            bool dealDirectDamageOnImpact = true)
        {
            Projectile = projectile != null
                ? projectile
                : throw new ArgumentNullException(nameof(projectile));
            if (attack.Attacker == null)
                throw new ArgumentException(
                    "A projectile attack must have an attacker.",
                    nameof(attack));
            if (direction.sqrMagnitude <= Mathf.Epsilon)
                throw new ArgumentException(
                    "A projectile direction must be non-zero.",
                    nameof(direction));

            Attack = attack;
            Origin = origin;
            Direction = direction.normalized;
            Destination = destination;
            OnImpact = onImpact;
            TravelEffectPrefab = travelEffectPrefab;
            DealDirectDamageOnImpact = dealDirectDamageOnImpact;
        }
    }
}
