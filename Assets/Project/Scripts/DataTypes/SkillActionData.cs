using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
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
    }

    [Serializable]
    public sealed class StatModifierSkillActionData : SkillActionData
    {
        public EquipmentStat stat;
        public int amount;
    }
}
