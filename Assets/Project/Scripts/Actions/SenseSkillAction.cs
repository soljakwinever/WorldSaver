using System;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Actions
{
    [CreateAssetMenu(fileName = "New Sense Skill Action", menuName = "Data/Skill Actions/Sense")]
    public sealed class SenseSkillAction : SkillAction
    {
        public override Type DataType => typeof(SenseSkillActionData);

        public override bool CanPerform(SkillActionContext context, SkillActionData data) =>
            data is SenseSkillActionData sense &&
            context.User != null && context.RevealFeatures != null &&
            sense.radius > 0f && sense.revealDuration > 0f;

        public override void Perform(SkillActionContext context, SkillActionData data)
        {
            var sense = (SenseSkillActionData)data;
            context.RevealFeatures(sense.radius, sense.revealDuration);
        }
    }
}
