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

    public enum EruptionOriginMode : byte
    {
        Caster,
        Target,
        CompletionPosition
    }

    public enum EruptionVisualStyle : byte
    {
        Default,
        FallingBoulder,
        Prefab
    }

    [Serializable]
    public sealed class EruptionDefinition
    {
        [Min(0.05f)] public float radius = 0.7f;
        [Min(0)] public int damage = 8;
        public bool includeAttackPotential;
        public PlayerAttackType attackType = PlayerAttackType.Magic;
        public LayerMask targetLayers = ~0;
        public EntityTag element;
        public EntityTag[] damageTags = Array.Empty<EntityTag>();
        public GameObject telegraphPrefab;
        public GameObject eruptionEffectPrefab;
        [Min(0f)] public float effectLifetime;
        public Color telegraphColor = new(0.12f, 0.04f, 0.02f, 0.52f);
        public Color fallbackEffectColor = new(0.3f, 0.2f, 0.12f, 1f);
        public EruptionVisualStyle visualStyle;
    }

    [Serializable]
    public sealed class EruptionPatternData
    {
        public EruptionOriginMode origin = EruptionOriginMode.CompletionPosition;
        public EruptionDefinition eruption = new();
        [Min(0f)] public float firstRingDistance = 1.75f;
        [Min(0f)] public float ringSpacing = 1.5f;
        [Min(1)] public int ringCount = 1;
        [Min(1)] public int eruptionsPerRing = 6;
        public float initialRotation;
        public float rotationOffsetPerRing = 30f;
        [Min(0f)] public float detonationDelay = 0.6f;
        [Min(0f)] public float delayPerRing = 0.2f;
    }

    [Serializable]
    public abstract class SkillActionData
    {
        [Tooltip("Reusable behavior; all per-skill parameters belong on this record.")]
        public SkillAction action;
        public SkillActionMode mode;
        [Tooltip("Negative uses the SkillData default duration.")]
        public float durationOverride = -1f;
        [Tooltip("Spawn the configured eruption pattern when this action reaches gameplay completion.")]
        public bool enableFinishEruptions;
        [Tooltip("Optional eruption pattern spawned when this action reaches gameplay completion.")]
        public EruptionPatternData finishEruptions;
    }

    [Serializable]
    public sealed class AttackSkillActionData : SkillActionData
    {
        [Min(0)] public int baseDamage;
        [Min(0f), Tooltip("Launch power per point of damage actually delivered. Zero disables knockback.")]
        public float knockbackPowerMultiplier;
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
        [Min(0f), Tooltip("Launch power per point of damage actually delivered. Zero disables knockback.")]
        public float knockbackPowerMultiplier;
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
        [Min(0f), Tooltip("Launch power per point of damage actually delivered. Zero disables knockback.")]
        public float knockbackImpulse;
        [Min(0f)] public float stunDuration;
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
        [Tooltip("Optional one-shot particle effect spawned at the impact center.")]
        public GameObject impactParticlePrefab;
        [Min(0f)] public float impactParticleLifetime;
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
    public sealed class EruptionPatternSkillActionData : SkillActionData
    {
        public EruptionPatternData pattern = new();
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
    public sealed class HeightSkillActionData : SkillActionData
    {
        [Min(0.01f)] public float peakHeight = 1f;
        [Min(0.01f)] public float travelDuration = 0.5f;
    }

    [Serializable]
    public sealed class JumpAttackSkillActionData : SkillActionData
    {
        [Min(0.01f)] public float peakHeight = 2.5f;
        [Min(0.01f)] public float ascentDuration = 0.45f;
        [Min(0f)] public float hoverDuration = 0.25f;
        [Min(0.01f)] public float descentDuration = 0.35f;
        [Min(0f)] public float maximumPredictionTime = 2f;
        public LayerMask blockingLayers = (1 << 0) | (1 << 6);
        [Min(0)] public int baseDamage = 10;
        [Min(0.1f)] public float impactRadius = 2f;
        [Min(0f)] public float knockbackImpulse = 35f;
        [Min(0f)] public float stunDuration = 0.8f;
        [Min(1f), Tooltip("Movement multiplier granted when the slam deals positive damage. One disables it.")]
        public float successfulHitMovementMultiplier = 1f;
        [Min(0f)] public float successfulHitMovementDuration;
        public LayerMask targetLayers = ~0;
        public PlayerAttackType attackType = PlayerAttackType.Melee;
        public GameObject impactParticlePrefab;
        [Min(0f)] public float impactParticleLifetime;
        [Tooltip("Optional camera shake played when the jump lands. Zero amplitude disables it.")]
        public ScreenShakeRequest screenShake;
        public EntityTag element;
        public EntityTag[] damageTags = Array.Empty<EntityTag>();
        public AnimationClip animationClip;
    }

    [Serializable]
    public sealed class SwallowSkillActionData : SkillActionData
    {
        public Vector2 boxSize = new(1.4f, 1f);
        [Min(0f)] public float forwardOffset = 0.75f;
        public LayerMask targetLayers = ~0;
        public bool allowPlayerTargets = true;
        public bool allowEnemyTargets;
        [Tooltip("After capturing an NPC, disengage and permanently despawn after leaving the screen.")]
        public bool retreatOffscreenAfterNpcCapture;
        public EntityTag[] requiredTargetTags = Array.Empty<EntityTag>();
        public EntityTag[] excludedTargetTags = Array.Empty<EntityTag>();
        [Range(0f, 1f)] public float captureChance = 0.1f;
        [Range(0f, 1f)] public float stunnedCaptureChance = 0.35f;
        [Range(0f, 1f)] public float enemyCaptureChance = 1f;
        [Header("Failed Capture")]
        [Min(0)] public int failedCaptureDamage;
        [Min(0f)] public float failedCaptureKnockbackImpulse;
        [Min(0f)] public float failedCaptureStunDuration;
        public PlayerAttackType failedCaptureAttackType = PlayerAttackType.Melee;
        public EntityTag[] failedCaptureDamageTags = Array.Empty<EntityTag>();
        [Min(1)] public int stomachDamage = 2;
        [Min(1)] public int damageIntervalTicks = 3;
        [Min(0f)] public float digestionHealingRatio = 1f;
        [Min(0f)] public float struggleLockDuration = 0.5f;
        [Min(0f)] public float struggleDecayPerSecond = 0.08f;
        [Min(0f)] public float fullStruggleGain = 0.2f;
        [Min(0f)] public float exhaustedStruggleGain = 0.05f;
        [Min(0f)] public float struggleEnergyCost = 20f;
        [Min(0f)] public float escapeDistance = 1.25f;
        [Min(0f)] public float escapeStunDuration = 1.5f;
        public LayerMask releaseBlockingLayers = (1 << 0) | (1 << 6);
        [Min(1f)] public float swallowedScaleMultiplier = 2f;
        [Min(0f)] public float scaleTransitionDuration = 0.2f;
        [Min(0.1f)] public float struggleBarWidth = 1.4f;
        [Min(0.02f)] public float struggleBarHeight = 0.14f;
        public Vector2 struggleBarOffset = new(0f, 1.5f);
        public Color struggleBarBackground = new(0.05f, 0.05f, 0.05f, 0.85f);
        public Color struggleBarFill = new(0.2f, 0.9f, 0.25f, 1f);
    }

    [Serializable]
    public sealed class SenseSkillActionData : SkillActionData
    {
        [Min(0.1f)] public float radius = 512f;
        [Min(0.1f)] public float revealDuration = 8f;
    }
}
