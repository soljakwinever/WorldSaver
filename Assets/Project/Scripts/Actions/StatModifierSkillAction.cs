using System;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Actions
{
    [CreateAssetMenu(fileName = "New Stat Modifier Skill Action", menuName = "Data/Skill Actions/Stat Modifier")]
    public sealed class StatModifierSkillAction : SkillAction
    {
        public override Type DataType => typeof(StatModifierSkillActionData);
        public override bool SupportsMode(SkillActionMode mode) => true;

        public override bool CanPerform(SkillActionContext context, SkillActionData data) =>
            data is StatModifierSkillActionData && context.ApplyModifier != null;

        public override void Perform(SkillActionContext context, SkillActionData data)
        {
            var modifier = (StatModifierSkillActionData)data;
            context.ApplyModifier(
                modifier.stat, modifier.amount,
                context.Skill.GetDuration(data), context.EffectKey);
        }

        public override void Grant(SkillActionContext context, SkillActionData data)
        {
            var modifier = (StatModifierSkillActionData)data;
            context.ApplyModifier(modifier.stat, modifier.amount, -1f, context.EffectKey);
        }

        public override void Revoke(SkillActionContext context, SkillActionData data) =>
            context.RemoveModifier?.Invoke(context.EffectKey);
    }
}
