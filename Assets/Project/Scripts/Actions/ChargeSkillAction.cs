using System;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Actions
{
    [CreateAssetMenu(fileName = "New Charge Skill Action", menuName = "Data/Skill Actions/Charge")]
    public sealed class ChargeSkillAction : SkillAction
    {
        public override Type DataType => typeof(ChargeSkillActionData);
        public override bool CompletesAsynchronously => true;

        public override bool CanPerform(SkillActionContext context, SkillActionData data) =>
            data is ChargeSkillActionData charge &&
            context.User != null && context.StartCharge != null &&
            charge.distance > 0f && charge.travelDuration > 0f &&
            charge.hitRadius > 0f;

        public override void Perform(SkillActionContext context, SkillActionData data)
        {
            var charge = (ChargeSkillActionData)data;
            context.PlayAnimation?.Invoke(charge.animationClip);
            context.StartCharge(charge, context);
        }
    }
}
