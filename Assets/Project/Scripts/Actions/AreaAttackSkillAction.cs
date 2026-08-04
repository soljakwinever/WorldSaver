using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;

namespace Project.Scripts.Actions
{
    [CreateAssetMenu(
        fileName = "New Area Attack Skill Action",
        menuName = "Data/Skill Actions/Area Attack")]
    public sealed class AreaAttackSkillAction : SkillAction
    {
        public override Type DataType => typeof(AreaAttackSkillActionData);

        public override bool CanPerform(SkillActionContext context, SkillActionData data) =>
            data is AreaAttackSkillActionData attack &&
            context.User != null && attack.radius > 0f;

        public override void Perform(SkillActionContext context, SkillActionData data)
        {
            AreaAttackSkillActionData attack = (AreaAttackSkillActionData)data;
            AreaAttackService service =
                UnityEngine.Object.FindFirstObjectByType<AreaAttackService>();
            if (service == null)
            {
                Debug.LogError("AreaAttackService is not installed.");
                return;
            }

            context.PlayAnimation?.Invoke(attack.animationClip);
            service.Execute(context, attack);
        }
    }
}
