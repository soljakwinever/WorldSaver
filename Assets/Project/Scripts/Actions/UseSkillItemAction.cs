using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;

namespace Project.Scripts.Actions
{
    /// <summary>
    /// Executes a skill from an item while supplying item-specific projectile
    /// and weapon-visual data.
    /// </summary>
    [CreateAssetMenu(
        fileName = "New Use Skill Item Action",
        menuName = "Data/Item Actions/Use Skill")]
    public sealed class UseSkillItemAction : ItemAction
    {
        public override bool CanPerform(ActionContext context)
        {
            return TryResolve(
                       context,
                       out SkillRuntime runtime,
                       out UseSkillItemActionData data) &&
                   runtime.CanUse(
                       data.skill,
                       context.Target,
                       context.TargetPosition,
                       projectile: data.projectile);
        }

        public override bool Perform(ActionContext context)
        {
            return TryResolve(
                       context,
                       out SkillRuntime runtime,
                       out UseSkillItemActionData data) &&
                   runtime.TryUseWithWeaponSprite(
                       data.skill,
                       context.Target,
                       context.TargetPosition,
                       data.weaponSpriteOverride,
                       projectile: data.projectile);
        }

        public override string GetDisplayName(ItemData item)
        {
            return TryGetData(item, out UseSkillItemActionData data) &&
                   data.skill != null
                ? data.skill.name
                : base.GetDisplayName(item);
        }

        public override string GetTooltip(ItemData item)
        {
            return TryGetData(item, out UseSkillItemActionData data) &&
                   data.skill != null
                ? data.skill.description
                : base.GetTooltip(item);
        }

        private static bool TryResolve(
            ActionContext context,
            out SkillRuntime runtime,
            out UseSkillItemActionData data)
        {
            data = null;
            runtime = context.User != null
                ? context.User.GetComponentInParent<SkillRuntime>()
                : null;
            return runtime != null && TryGetData(context.Item, out data) &&
                   data.skill != null;
        }

        private static bool TryGetData(
            ItemData item,
            out UseSkillItemActionData data)
        {
            if (item != null && item.TryGetActionData(out data))
                return true;
            data = null;
            return false;
        }
    }
}
