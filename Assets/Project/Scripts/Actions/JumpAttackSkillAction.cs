using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;

namespace Project.Scripts.Actions
{
    [CreateAssetMenu(
        fileName = "New Jump Attack Skill Action",
        menuName = "Data/Skill Actions/Jump Attack")]
    public sealed class JumpAttackSkillAction : SkillAction
    {
        public override Type DataType => typeof(JumpAttackSkillActionData);
        public override bool CompletesAsynchronously => true;

        public override bool CanPerform(
            SkillActionContext context,
            SkillActionData data) =>
            data is JumpAttackSkillActionData jump &&
            JumpAttackController.CanLand(
                context.User,
                context.Target,
                context.TargetPosition,
                jump);

        public override void Perform(
            SkillActionContext context,
            SkillActionData data)
        {
            JumpAttackSkillActionData jump =
                (JumpAttackSkillActionData)data;
            JumpAttackController controller =
                context.User.GetComponent<JumpAttackController>() ??
                context.User.AddComponent<JumpAttackController>();
            context.PlayAnimation?.Invoke(jump.animationClip);
            controller.TryBegin(context, jump);
        }
    }
}
