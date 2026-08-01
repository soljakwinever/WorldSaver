using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    public readonly struct SkillActionContext
    {
        public GameObject User { get; }
        public GameObject Target { get; }
        public Vector3 TargetPosition { get; }
        public SkillData Skill { get; }
        public int ActionIndex { get; }
        public Func<GameObject, AttackContext, int> DealDamage { get; }
        public Action<AnimationClip> PlayAnimation { get; }
        public Action<EquipmentStat, int, float, string> ApplyModifier { get; }
        public Action<string> RemoveModifier { get; }

        public string EffectKey => $"{Skill?.persistentId}:{ActionIndex}";

        public SkillActionContext(
            GameObject user, GameObject target, Vector3 targetPosition,
            SkillData skill, int actionIndex,
            Func<GameObject, AttackContext, int> dealDamage,
            Action<AnimationClip> playAnimation,
            Action<EquipmentStat, int, float, string> applyModifier,
            Action<string> removeModifier)
        {
            User = user;
            Target = target;
            TargetPosition = targetPosition;
            Skill = skill;
            ActionIndex = actionIndex;
            DealDamage = dealDamage;
            PlayAnimation = playAnimation;
            ApplyModifier = applyModifier;
            RemoveModifier = removeModifier;
        }
    }

    public abstract class SkillAction : ScriptableObject
    {
        public abstract Type DataType { get; }
        public virtual bool SupportsMode(SkillActionMode mode) =>
            mode == SkillActionMode.Active;
        public abstract bool CanPerform(SkillActionContext context, SkillActionData data);
        public abstract void Perform(SkillActionContext context, SkillActionData data);
        public virtual void Grant(SkillActionContext context, SkillActionData data) { }
        public virtual void Revoke(SkillActionContext context, SkillActionData data) { }

        public bool Accepts(SkillActionData data) =>
            data != null && DataType.IsInstanceOfType(data);
    }
}
