using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Actions
{
    [CreateAssetMenu(fileName = "New Swallow Skill Action",
        menuName = "Data/Skill Actions/Swallow")]
    public sealed class SwallowSkillAction : SkillAction
    {
        public override Type DataType => typeof(SwallowSkillActionData);

        public override bool CanPerform(SkillActionContext context,
            SkillActionData data) => data is SwallowSkillActionData swallow &&
            context.User != null && swallow.boxSize.x > 0f &&
            swallow.boxSize.y > 0f &&
            !SwallowedStateController.OwnerHasSwallowedTarget(context.User);

        public override void Perform(SkillActionContext context,
            SkillActionData data)
        {
            var swallow = (SwallowSkillActionData)data;
            Vector2 direction = ForwardBoxAttackSkillAction.ResolveDirection(context);
            Vector2 center = (Vector2)context.User.transform.position +
                             direction * swallow.forwardOffset;
            float angle = Mathf.Atan2(-direction.x, direction.y) * Mathf.Rad2Deg;
            Collider2D[] hits = Physics2D.OverlapBoxAll(
                center, swallow.boxSize, angle, swallow.targetLayers);
            HashSet<GameObject> candidates = new();
            List<GameObject> orderedCandidates = new();
            foreach (Collider2D hit in hits)
            {
                PlayerDataController player = hit != null
                    ? hit.GetComponentInParent<PlayerDataController>()
                    : null;
                EnemyRuntime enemy = hit != null
                    ? hit.GetComponentInParent<EnemyRuntime>()
                    : null;
                GameObject target = player != null && swallow.allowPlayerTargets
                    ? player.gameObject
                    : enemy != null && swallow.allowEnemyTargets
                        ? enemy.gameObject
                        : null;
                if (target == null || target == context.User ||
                    !candidates.Add(target) || !MatchesTags(target, swallow))
                    continue;
                orderedCandidates.Add(target);
            }

            GameObject intendedTarget = ResolveCandidate(context.Target, swallow);
            if (intendedTarget != null && orderedCandidates.Remove(intendedTarget))
                orderedCandidates.Insert(0, intendedTarget);

            foreach (GameObject target in orderedCandidates)
            {
                PlayerDataController player =
                    target.GetComponent<PlayerDataController>();
                EnemyRuntime enemy = target.GetComponent<EnemyRuntime>();
                bool stunned = false;
                foreach (MonoBehaviour behaviour in target.GetComponents<MonoBehaviour>())
                    if (behaviour is IStunState stunState && stunState.IsStunned)
                    {
                        stunned = true;
                        break;
                    }
                float chance = enemy != null
                    ? swallow.enemyCaptureChance
                    : stunned
                    ? swallow.stunnedCaptureChance
                    : swallow.captureChance;
                bool captured = false;
                if (UnityEngine.Random.value < Mathf.Clamp01(chance))
                {
                    SwallowedStateController state =
                        target.GetComponent<SwallowedStateController>() ??
                        target.AddComponent<SwallowedStateController>();
                    captured = state.TrySwallow(context.User, swallow);
                }
                if (!captured)
                    ApplyFailedCapture(context, target, swallow);
                return;
            }
        }

        private static void ApplyFailedCapture(
            SkillActionContext context,
            GameObject target,
            SwallowSkillActionData settings)
        {
            if (context.DealDamage == null ||
                settings.failedCaptureDamage <= 0)
                return;

            List<EntityTag> tags = new();
            IEntityDamageSource source =
                context.User.GetComponentInParent<IEntityDamageSource>();
            if (source?.DamageTags != null)
                tags.AddRange(source.DamageTags);
            if (context.Skill?.tags != null)
                tags.AddRange(context.Skill.tags);
            if (settings.failedCaptureDamageTags != null)
                tags.AddRange(settings.failedCaptureDamageTags);

            int delivered = context.DealDamage(
                target,
                new AttackContext(
                    context.User,
                    null,
                    settings.failedCaptureDamage,
                    source?.DamageSource ?? EntityDamageSource.Skill,
                    tags,
                    context.Skill,
                    settings.failedCaptureAttackType));
            if (delivered <= 0)
                return;
            CombatControlUtility.Apply(
                target,
                context.User.transform.position,
                settings.failedCaptureKnockbackImpulse,
                settings.failedCaptureStunDuration);
        }

        private static GameObject ResolveCandidate(
            GameObject value,
            SwallowSkillActionData settings)
        {
            if (value == null)
                return null;
            PlayerDataController player =
                value.GetComponentInParent<PlayerDataController>();
            if (player != null && settings.allowPlayerTargets)
                return player.gameObject;
            EnemyRuntime enemy = value.GetComponentInParent<EnemyRuntime>();
            return enemy != null && settings.allowEnemyTargets
                ? enemy.gameObject
                : null;
        }

        private static bool MatchesTags(
            GameObject target,
            SwallowSkillActionData settings)
        {
            IEntityTagProvider provider =
                target.GetComponentInParent<IEntityTagProvider>();
            IReadOnlyList<EntityTag> tags = provider?.EntityTags ??
                                            Array.Empty<EntityTag>();
            foreach (EntityTag excluded in settings.excludedTargetTags ??
                         Array.Empty<EntityTag>())
                if (excluded != null && Contains(tags, excluded))
                    return false;
            foreach (EntityTag required in settings.requiredTargetTags ??
                         Array.Empty<EntityTag>())
                if (required != null && !Contains(tags, required))
                    return false;
            return true;
        }

        private static bool Contains(
            IReadOnlyList<EntityTag> tags,
            EntityTag expected)
        {
            for (int i = 0; i < tags.Count; i++)
                if (tags[i] == expected)
                    return true;
            return false;
        }
    }
}
