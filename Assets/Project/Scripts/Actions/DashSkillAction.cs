using System;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Actions
{
    [CreateAssetMenu(fileName = "New Dash Skill Action", menuName = "Data/Skill Actions/Dash")]
    public sealed class DashSkillAction : SkillAction
    {
        public override Type DataType => typeof(DashSkillActionData);

        public override bool CanPerform(SkillActionContext context, SkillActionData data) =>
            data is DashSkillActionData && context.StartDash != null;

        public override void Perform(SkillActionContext context, SkillActionData data) =>
            context.StartDash((DashSkillActionData)data, context);
    }
}
