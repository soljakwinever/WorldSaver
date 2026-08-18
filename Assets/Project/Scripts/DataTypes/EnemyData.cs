using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [Serializable]
    public sealed class DropData
    {
        public ItemData item;
        [Range(0f, 1f)] public float dropChance = 1f;
        [Min(1)] public int rolls = 1;
        [Min(1)] public int minimumAmount = 1;
        [Min(1)] public int maximumAmount = 1;

        public bool PassesDropChance(float roll)
        {
            if (dropChance <= 0f)
                return false;
            if (dropChance >= 1f)
                return true;

            return roll < dropChance;
        }

        public int RollAmount() =>
            UnityEngine.Random.Range(minimumAmount, maximumAmount + 1);
    }

    public enum EnemySkillConditionType : byte
    {
        Always,
        DistanceToTarget,
        All,
        Any,
        Not
    }

    [Serializable]
    public sealed class EnemySkillCondition
    {
        public EnemySkillConditionType type;
        [Min(0f)] public float distance = 1f;
        public EnemySkillCondition[] conditions =
            Array.Empty<EnemySkillCondition>();
    }

    [Serializable]
    public sealed class ConditionalEnemySkill
    {
        public SkillData skill;
        [Tooltip("Projectile supplied to projectile actions when this skill is selected.")]
        public ProjectileData projectile;
        [Tooltip("Optional weapon swing override for this conditional skill. Falls back to the enemy's basic-attack swing when empty.")]
        public WeaponSwingAnimation weaponSwing;
        public EnemySkillCondition condition = new();
    }

    [CreateAssetMenu(
        fileName = "New Enemy Data",
        menuName = "Data/Enemy Data",
        order = 0)]
    public sealed class EnemyData : ScriptableObject
    {
        [Header("Stats")]
        [Min(1)] public int hp = 10;
        [Min(0)] public int attack = 1;
        [Min(0)] public int defense;
        [Min(0)] public int experienceValue;
        [Range(0f, 1f), Tooltip("Projectile accuracy. One is perfect aim; lower values add angular spread.")]
        public float accuracy = 1f;
        [Range(0f, 1f), Tooltip("How dangerous this enemy is for adaptive audio.")]
        public float dangerLevel;
        [Range(0f, 1f), Tooltip("How strongly this enemy's danger contributes to adaptive audio.")]
        public float dangerWeight = 1f;

        [Header("Movement")]
        [Min(0f)] public float movementSpeed = 2f;
        [Min(1f)] public float sprintMultiplier = 1.5f;

        [Header("Presentation and AI")]
        [Tooltip("Prefab spawned for this enemy. It may already contain health and AI components.")]
        public GameObject visual;
        public BehaviourTreeData behaviourTree;

        [Header("Combat")]
        [Tooltip("Skill used when this enemy's AI executes the Attack Target action.")]
        public SkillData NormalAttack;
        [Tooltip("Weapon presentation used by the basic attack. This overrides swing animations configured inside the skill.")]
        public WeaponSwingAnimation BasicAttackWeaponSwing;
        [Tooltip("Priority-ordered skills selected by conditional-skill AI nodes.")]
        public ConditionalEnemySkill[] conditionalSkills =
            Array.Empty<ConditionalEnemySkill>();

        [Header("Damage")]
        [Tooltip("Tags supplied when this enemy damages an entity.")]
        public EntityTag[] damageTags = Array.Empty<EntityTag>();

        [Header("Death Drops")]
        public DropData[] drops = Array.Empty<DropData>();
        [Min(0f)] public float dropExplosionImpulse = 2.5f;
        [Range(0f, 1f)] public float dropExplosionVariation = 0.25f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            hp = Mathf.Max(1, hp);
            attack = Mathf.Max(0, attack);
            defense = Mathf.Max(0, defense);
            experienceValue = Mathf.Max(0, experienceValue);
            accuracy = Mathf.Clamp01(accuracy);
            dangerLevel = Mathf.Clamp01(dangerLevel);
            dangerWeight = Mathf.Clamp01(dangerWeight);
            movementSpeed = Mathf.Max(0f, movementSpeed);
            sprintMultiplier = Mathf.Max(1f, sprintMultiplier);
            dropExplosionImpulse = Mathf.Max(0f, dropExplosionImpulse);
            drops ??= Array.Empty<DropData>();
            damageTags ??= Array.Empty<EntityTag>();
            conditionalSkills ??= Array.Empty<ConditionalEnemySkill>();

            foreach (DropData drop in drops)
            {
                if (drop == null)
                    continue;

                drop.rolls = Mathf.Max(1, drop.rolls);
                drop.minimumAmount = Mathf.Max(1, drop.minimumAmount);
                drop.maximumAmount =
                    Mathf.Max(drop.minimumAmount, drop.maximumAmount);
            }
        }
#endif
    }
}
