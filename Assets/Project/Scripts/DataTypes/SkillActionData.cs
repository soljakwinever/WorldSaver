using System;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    public enum ChargeDirectionMode : byte
    {
        PlayerFacing,
        TowardCursor
    }

    [Serializable]
    public abstract class SkillActionData
    {
        [Tooltip("Reusable behavior; all per-skill parameters belong on this record.")]
        public SkillAction action;
        public SkillActionMode mode;
        [Tooltip("Negative uses the SkillData default duration.")]
        public float durationOverride = -1f;
    }

    [Serializable]
    public sealed class AttackSkillActionData : SkillActionData
    {
        [Min(0)] public int baseDamage;
        public PlayerAttackType attackType = PlayerAttackType.Magic;
        [Tooltip("Optional elemental tag included in damage-source tags.")]
        public EntityTag element;
        public EntityTag[] damageTags = Array.Empty<EntityTag>();
        public AnimationClip animationClip;
        [Tooltip("Optional data-driven weapon visual and per-frame hurt boxes.")]
        public WeaponSwingAnimation weaponSwing;
    }

    [Serializable]
    public sealed class ForwardBoxAttackSkillActionData : SkillActionData
    {
        [Min(0)] public int baseDamage;
        public PlayerAttackType attackType = PlayerAttackType.Melee;
        [Tooltip("Width and forward depth of the attack box.")]
        public Vector2 boxSize = new(1f, 1f);
        [Min(0f), Tooltip("Distance from the user to the center of the box.")]
        public float forwardOffset = 0.75f;
        public LayerMask targetLayers = ~0;
        public EntityTag element;
        public EntityTag[] damageTags = Array.Empty<EntityTag>();
        public AnimationClip animationClip;
    }

    [Serializable]
    public sealed class AreaAttackSkillActionData : SkillActionData
    {
        [Min(0)] public int baseDamage;
        [Min(0.1f)] public float radius = 2f;
        public PlayerAttackType attackType = PlayerAttackType.Magic;
        public LayerMask targetLayers = ~0;
        [Tooltip("Also damages wall tiles inside the radius.")]
        public bool damageWalls = true;
        public WallDestructionType wallDestructionType =
            WallDestructionType.Destroyed;
        [Tooltip("Optional replacement for ground cells inside the radius.")]
        public TileData groundTile;
        [Tooltip("Optional world-space visual instantiated at the impact point.")]
        public GameObject decalPrefab;
        [Min(0f), Tooltip("Real-time lifetime when tick persistence is disabled. Zero leaves the decal until its own effect removes it.")]
        public float decalLifetimeSeconds;
        [Tooltip("Use world ticks instead of seconds for the decal lifetime.")]
        public bool persistentDecal;
        [Min(1)] public int decalLifetimeTicks = 1;
        public EntityTag element;
        public EntityTag[] damageTags = Array.Empty<EntityTag>();
        public AnimationClip animationClip;
    }

    [Serializable]
    public sealed class ProjectileSkillActionData : SkillActionData
    {
        [Min(0)] public int baseDamage = 1;
        public PlayerAttackType attackType = PlayerAttackType.Ranged;
        [Tooltip("Local-space offset from the caster used as the launch origin.")]
        public Vector2 launchOffset;
        [Tooltip("Use caster facing instead of aiming toward the target position.")]
        public bool useFacingDirection;
        [Tooltip("Lead a moving target based on its Rigidbody2D velocity and the projectile speed.")]
        public bool predictTargetMovement;
        [Tooltip("Use the target position captured when the skill was committed instead of tracking the target at release.")]
        public bool useLockedTargetPosition;
        [Min(0f), Tooltip("Maximum number of seconds to lead the target.")]
        public float maximumPredictionTime = 2f;
        [Tooltip("Consume one inventory projectile when the caster supplied one from inventory.")]
        public bool consumeProjectile = true;
        public EntityTag element;
        public EntityTag[] damageTags = Array.Empty<EntityTag>();
        public AnimationClip animationClip;
        [Tooltip("Optional 360-degree weapon telegraph. The projectile launches on its release frame.")]
        public WeaponSwingAnimation weaponSwing;
    }

    [Serializable]
    public sealed class ExplosiveProjectileSkillActionData : SkillActionData
    {
        [Tooltip("Projectile movement, collision, and prefab settings.")]
        public ProjectileData projectile;
        [Min(0)] public int baseDamage = 12;
        [Min(0.1f)] public float explosionRadius = 2.5f;
        public PlayerAttackType attackType = PlayerAttackType.Magic;
        public Vector2 launchOffset;
        public LayerMask targetLayers = ~0;
        public bool damageWalls;
        public WallDestructionType wallDestructionType =
            WallDestructionType.Destroyed;
        public EntityTag element;
        public EntityTag[] damageTags = Array.Empty<EntityTag>();
        [Tooltip("Optional effect parented to the projectile while it travels.")]
        public GameObject projectileParticlePrefab;
        [Tooltip("Optional one-shot effect spawned at the explosion point.")]
        public GameObject explosionParticlePrefab;
        [Min(0f), Tooltip("Lifetime of the spawned explosion effect. Zero uses its particle duration.")]
        public float explosionParticleLifetime;
        [Tooltip("Optional world-space mark spawned beneath the explosion.")]
        public GameObject decalPrefab;
        [Min(0f)] public float decalLifetimeSeconds = 8f;
        public bool persistentDecal;
        [Min(1)] public int decalLifetimeTicks = 1;
        public AnimationClip animationClip;
    }

    [Serializable]
    public sealed class StatModifierSkillActionData : SkillActionData
    {
        public EquipmentStat stat;
        public int amount;
    }

    [Serializable]
    public sealed class ChargeSkillActionData : SkillActionData
    {
        [Min(0)] public int baseDamage = 4;
        public PlayerAttackType attackType = PlayerAttackType.Melee;
        [Tooltip("Choose between the actor's last facing direction and the direction from the actor to the target/cursor position.")]
        public ChargeDirectionMode directionMode = ChargeDirectionMode.PlayerFacing;
        [Min(0.1f)] public float distance = 5f;
        [Min(0.02f)] public float travelDuration = 0.25f;
        [Min(0.05f)] public float hitRadius = 0.6f;
        [Min(0f)] public float knockbackImpulse = 30f;
        [Tooltip("Layers that stop the charge. Zero preserves legacy unobstructed charges.")]
        public LayerMask blockingLayers;
        public EntityTag element;
        public EntityTag[] damageTags = Array.Empty<EntityTag>();
        public AnimationClip animationClip;
    }

    [Serializable]
    public sealed class DashSkillActionData : SkillActionData
    {
        [Min(1f), Tooltip("Multiplier applied to the actor's normal movement speed while dashing.")]
        public float speedMultiplier = 2.5f;
        [Min(0.01f), Tooltip("Seconds between afterimages.")]
        public float afterimageInterval = 0.08f;
        [Min(0.01f), Tooltip("How long each afterimage takes to fade out.")]
        public float afterimageFadeDuration = 0.35f;
        [Tooltip("Tint applied to the copied player sprites.")]
        public Color afterimageTint = new(0.45f, 0.85f, 1f, 0.65f);
    }

    [Serializable]
    public sealed class SenseSkillActionData : SkillActionData
    {
        [Min(0.1f)] public float radius = 512f;
        [Min(0.1f)] public float revealDuration = 8f;
    }
}
