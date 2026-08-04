using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Actions
{
    [CreateAssetMenu(
        fileName = "New Forward Box Attack Skill Action",
        menuName = "Data/Skill Actions/Forward Box Attack")]
    public sealed class ForwardBoxAttackSkillAction : SkillAction
    {
        public override Type DataType =>
            typeof(ForwardBoxAttackSkillActionData);

        public override bool CanPerform(
            SkillActionContext context,
            SkillActionData data) =>
            data is ForwardBoxAttackSkillActionData attack &&
            context.User != null && context.DealDamage != null &&
            attack.boxSize.x > 0f && attack.boxSize.y > 0f;

        public override void Perform(
            SkillActionContext context,
            SkillActionData data)
        {
            var attack = (ForwardBoxAttackSkillActionData)data;
            Vector2 direction = ResolveDirection(context);
            Vector2 center = (Vector2)context.User.transform.position +
                             direction * Mathf.Max(0f, attack.forwardOffset);
            float angle = Mathf.Atan2(-direction.x, direction.y) *
                          Mathf.Rad2Deg;
            Collider2D[] hits = Physics2D.OverlapBoxAll(
                center,
                attack.boxSize,
                angle,
                attack.targetLayers);

            IEntityDamageSource damageSource =
                context.User.GetComponentInParent<IEntityDamageSource>();
            List<EntityTag> tags = BuildTags(context, attack, damageSource);
            HashSet<IDamageable> damaged = new();

            context.PlayAnimation?.Invoke(attack.animationClip);
            foreach (Collider2D hit in hits)
            {
                if (hit == null ||
                    hit.transform.IsChildOf(context.User.transform))
                    continue;

                IDamageable target = hit.GetComponentInParent<IDamageable>() ??
                                     hit.GetComponentInChildren<IDamageable>();
                if (target == null || !damaged.Add(target))
                    continue;

                GameObject targetObject = target is Component component
                    ? component.gameObject
                    : hit.gameObject;
                context.DealDamage(
                    targetObject,
                    new AttackContext(
                        context.User,
                        null,
                        checked(
                            Mathf.Max(0, attack.baseDamage) +
                            context.AttackPotential),
                        damageSource?.DamageSource ?? EntityDamageSource.Skill,
                        tags,
                        context.Skill,
                        attack.attackType));
            }
        }

        internal static Vector2 ResolveDirection(SkillActionContext context)
        {
            Vector2 direction =
                context.User.GetComponentInParent<ISkillFacing>()?
                    .FacingDirection ?? Vector2.zero;
            if (direction.sqrMagnitude < 0.0001f && context.Target != null)
            {
                direction = context.Target.transform.position -
                            context.User.transform.position;
            }

            return direction.sqrMagnitude < 0.0001f
                ? Vector2.down
                : direction.normalized;
        }

        private static List<EntityTag> BuildTags(
            SkillActionContext context,
            ForwardBoxAttackSkillActionData attack,
            IEntityDamageSource damageSource)
        {
            List<EntityTag> tags = new();
            if (damageSource?.DamageTags != null)
                tags.AddRange(damageSource.DamageTags);
            if (context.Skill?.tags != null)
                tags.AddRange(context.Skill.tags);
            if (attack.damageTags != null)
                tags.AddRange(attack.damageTags);
            if (attack.element != null)
                tags.Add(attack.element);
            return tags;
        }
    }
}
