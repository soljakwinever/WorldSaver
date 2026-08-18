using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using Project.Scripts.Gameplay;
using UnityEngine;

namespace Project.Scripts.Actions
{
    [CreateAssetMenu(fileName = "New Attack Skill Action", menuName = "Data/Skill Actions/Attack")]
    public sealed class AttackSkillAction : SkillAction
    {
        public override Type DataType => typeof(AttackSkillActionData);

        public override bool CanPerform(SkillActionContext context, SkillActionData data)
        {
            if (data is not AttackSkillActionData attack ||
                attack.baseDamage < 0 || context.User == null ||
                context.Target == null || context.DealDamage == null)
                return false;

            WeaponSwingController swing =
                context.User.GetComponentInParent<WeaponSwingController>();
            WeaponSwingAnimation weaponSwing = context.User
                .GetComponentInParent<SkillRuntime>()?
                .WeaponSwingOverride ?? attack.weaponSwing;
            return weaponSwing == null || swing == null || !swing.IsSwinging;
        }

        public override void Perform(SkillActionContext context, SkillActionData data)
        {
            var attack = (AttackSkillActionData)data;
            List<EntityTag> tags = new();
            IEntityDamageSource damageSource =
                context.User.GetComponentInParent<IEntityDamageSource>();
            if (damageSource?.DamageTags != null)
                tags.AddRange(damageSource.DamageTags);
            if (context.Skill?.tags != null) tags.AddRange(context.Skill.tags);
            if (attack.damageTags != null) tags.AddRange(attack.damageTags);
            if (attack.element != null) tags.Add(attack.element);

            AttackContext attackContext = new(
                context.User, null,
                checked(attack.baseDamage + context.AttackPotential),
                damageSource?.DamageSource ?? EntityDamageSource.Skill, tags,
                context.Skill, attack.attackType);

            context.PlayAnimation?.Invoke(attack.animationClip);
            SkillRuntime skillRuntime =
                context.User.GetComponentInParent<SkillRuntime>();
            WeaponSwingAnimation weaponSwing =
                skillRuntime?.WeaponSwingOverride ?? attack.weaponSwing;
            if (weaponSwing == null)
            {
                int delivered = context.DealDamage(context.Target, attackContext);
                CombatControlUtility.ApplyKnockback(
                    context.Target,
                    context.User.transform.position,
                    delivered,
                    attack.knockbackPowerMultiplier);
                return;
            }

            WeaponSwingController controller =
                context.User.GetComponentInParent<WeaponSwingController>() ??
                context.User.AddComponent<WeaponSwingController>();
            HashSet<IDamageable> damaged = new();
            controller.TryPlay(
                weaponSwing,
                context.Target.transform.position,
                hit =>
                {
                    IDamageable target = hit.GetComponentInParent<IDamageable>() ??
                                         hit.GetComponentInChildren<IDamageable>();
                    if (target == null || !damaged.Add(target))
                        return;
                    GameObject targetObject = target is Component component
                        ? component.gameObject
                        : hit.gameObject;
                    int delivered = context.DealDamage(targetObject, attackContext);
                    CombatControlUtility.ApplyKnockback(
                        targetObject,
                        context.User.transform.position,
                        delivered,
                        attack.knockbackPowerMultiplier);
                },
                trackedTarget: context.Target.transform,
                spriteOverride: skillRuntime?.WeaponSpriteOverride);
        }
    }
}
