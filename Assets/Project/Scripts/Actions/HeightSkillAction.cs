using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;

namespace Project.Scripts.Actions
{
    [CreateAssetMenu(fileName = "New Height Skill Action", menuName = "Data/Skill Actions/Height")]
    public sealed class HeightSkillAction : SkillAction
    {
        public override Type DataType => typeof(HeightSkillActionData);

        public override bool CanPerform(SkillActionContext context, SkillActionData data) =>
            data is HeightSkillActionData && context.Target != null &&
            context.Target.GetComponentInParent<Project.Scripts.Interface.IHasShadow>() != null;

        public override void Perform(SkillActionContext context, SkillActionData data)
        {
            var height = (HeightSkillActionData)data;
            FalseHeightController.TryLaunch(
                context.Target, height.peakHeight, height.travelDuration);
        }
    }
}
