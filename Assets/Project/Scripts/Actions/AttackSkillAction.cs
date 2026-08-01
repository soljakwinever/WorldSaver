using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Actions
{
    [CreateAssetMenu(fileName = "New Attack Skill Action", menuName = "Data/Skill Actions/Attack")]
    public sealed class AttackSkillAction : SkillAction
    {
        public override Type DataType => typeof(AttackSkillActionData);

        public override bool CanPerform(SkillActionContext context, SkillActionData data) =>
            data is AttackSkillActionData attack && attack.baseDamage >= 0 &&
            context.User != null && context.Target != null && context.DealDamage != null;

        public override void Perform(SkillActionContext context, SkillActionData data)
        {
            var attack = (AttackSkillActionData)data;
            List<EntityTag> tags = new();
            if (context.Skill?.tags != null) tags.AddRange(context.Skill.tags);
            if (attack.damageTags != null) tags.AddRange(attack.damageTags);
            if (attack.element != null) tags.Add(attack.element);

            context.PlayAnimation?.Invoke(attack.animationClip);
            context.DealDamage(
                context.Target,
                new AttackContext(
                    context.User, null, attack.baseDamage,
                    EntityDamageSource.Skill, tags,
                    context.Skill, attack.attackType));
        }
    }
}
