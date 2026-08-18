using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    public enum PlayerAttackType
    {
        Melee = 0,
        Ranged = 1,
        Magic = 2
    }

    public enum SkillPowerMode : byte
    {
        Additive,
        Multiplier
    }

    public enum DamageCause : byte
    {
        Direct,
        Digestion
    }

    [Flags]
    public enum EntityDamageSource
    {
        None = 0,
        Unspecified = 1 << 0,
        Tool = 1 << 1,
        Enemy = 1 << 2,
        Environment = 1 << 3,
        Skill = 1 << 4,
        All = Unspecified | Tool | Enemy | Environment | Skill
    }

    [Serializable]
    public sealed class EntityDamageRule
    {
        [Tooltip("Attack origins accepted by this rule.")]
        public EntityDamageSource sources =
            EntityDamageSource.Tool | EntityDamageSource.Enemy;

        [Tooltip("If non-empty, the damage source must have at least one of these tags.")]
        public EntityTag[] sourceTags = Array.Empty<EntityTag>();

        public bool Allows(AttackContext context)
        {
            if ((sources & context.Source) == 0)
                return false;
            if (sourceTags == null || sourceTags.Length == 0)
                return true;

            foreach (EntityTag required in sourceTags)
            {
                if (required != null && context.HasSourceTag(required))
                    return true;
            }

            return false;
        }
    }

    /// <summary>Describes the source and strength of an attack.</summary>
    public readonly struct AttackContext
    {
        public GameObject Attacker { get; }
        public ToolData Weapon { get; }
        public SkillData Skill { get; }
        public int Force { get; }
        public EntityDamageSource Source { get; }
        public IReadOnlyList<EntityTag> SourceTags { get; }
        public PlayerAttackType AttackType { get; }
        public SkillPowerMode SkillPowerMode { get; }
        public DamageCause Cause { get; }

        public AttackContext(GameObject attacker, ToolData weapon, int force)
            : this(
                attacker,
                weapon,
                force,
                weapon != null
                    ? EntityDamageSource.Tool
                    : EntityDamageSource.Unspecified,
                weapon?.DamageTags)
        {
        }

        public AttackContext(
            GameObject attacker,
            ToolData weapon,
            int force,
            EntityDamageSource source,
            IEnumerable<EntityTag> sourceTags = null,
            DamageCause cause = DamageCause.Direct)
        {
            if (attacker == null)
                throw new ArgumentNullException(nameof(attacker));
            if (force < 0)
                throw new ArgumentOutOfRangeException(nameof(force));

            Attacker = attacker;
            Weapon = weapon;
            Force = force;
            Source = source;
            Skill = null;
            AttackType = weapon != null ? weapon.AttackType : PlayerAttackType.Melee;
            SkillPowerMode = SkillPowerMode.Additive;
            Cause = cause;
            SourceTags = sourceTags?
                .Where(tag => tag != null)
                .Distinct()
                .ToArray() ?? Array.Empty<EntityTag>();
        }

        public AttackContext(
            GameObject attacker, ToolData weapon, int force,
            EntityDamageSource source, IEnumerable<EntityTag> sourceTags,
            SkillData skill, PlayerAttackType attackType,
            SkillPowerMode skillPowerMode = SkillPowerMode.Additive,
            DamageCause cause = DamageCause.Direct)
        {
            if (attacker == null) throw new ArgumentNullException(nameof(attacker));
            if (force < 0) throw new ArgumentOutOfRangeException(nameof(force));
            Attacker = attacker;
            Weapon = weapon;
            Force = force;
            Source = source;
            Skill = skill;
            AttackType = attackType;
            SkillPowerMode = skillPowerMode;
            Cause = cause;
            SourceTags = sourceTags?.Where(tag => tag != null).Distinct().ToArray()
                         ?? Array.Empty<EntityTag>();
        }

        public bool HasSourceTag(EntityTag tag)
        {
            if (tag == null)
                return false;
            foreach (EntityTag sourceTag in SourceTags)
            {
                if (sourceTag == tag)
                    return true;
            }
            return false;
        }
    }
}
