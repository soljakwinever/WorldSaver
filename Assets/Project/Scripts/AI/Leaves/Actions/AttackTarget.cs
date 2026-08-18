using System;
using Project.Scripts.AI.GraphEditor;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using Project.Scripts.Gameplay;
using Project.Scripts.AI.Leaves.Sensors;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Actions
{
    [Serializable, AiNode(Name = "Attack Target", Group = "Actions")]
    public sealed class AttackTarget : AiNode
    {
        [SerializeField, InputPort("Target")]
        private AiKeys.Key targetKey = AiKeys.Key.Target;

        [SerializeField, Min(0f), InputPort("Maximum Range")]
        private float maximumRange = 1f;

        [NonSerialized] private int attackSequence;

        protected override void OnEnter()
        {
            attackSequence = 0;
        }

        protected override NodeState OnTick()
        {
            GameObject attacker = Blackboard.GetOrDefault(AiKeys.Self);
            if (attacker == null)
                return NodeState.Failure;

            EnemyAttackController controller =
                attacker.GetComponentInParent<EnemyAttackController>();
            if (attackSequence > 0 && controller != null)
            {
                if (controller.TryGetResult(attackSequence, out bool succeeded))
                    return succeeded ? NodeState.Success : NodeState.Failure;
                if (controller.CurrentSequence == attackSequence &&
                    controller.IsAttacking)
                    return NodeState.Running;
                return NodeState.Failure;
            }

            if (!TryResolveTarget(targetKey, attacker, out Transform target,
                    out GameObject targetObject))
                return NodeState.Failure;

            if (!Blackboard.TryGet(AiKeys.EnemyData, out EnemyData enemyData) ||
                enemyData?.NormalAttack == null)
                return NodeState.Failure;

            int attackPotential = Mathf.Max(
                0,
                Blackboard.GetOrDefault(AiKeys.Attack));

            ISkillRuntime skillRuntime = attacker.GetComponentInParent<ISkillRuntime>();
            if (skillRuntime == null)
                return NodeState.Failure;

            ConditionalEnemySkill selected = SelectAttack(
                enemyData, attacker, target, targetObject,
                skillRuntime, attackPotential);
            if (selected != null)
            {
                if (controller == null)
                    controller = attacker.AddComponent<EnemyAttackController>();
                return controller.TryBegin(
                    selected, targetObject, attackPotential, out attackSequence)
                    ? NodeState.Running
                    : NodeState.Failure;
            }

            float range = Mathf.Max(0f, maximumRange);
            if ((attacker.transform.position - target.position).sqrMagnitude >
                range * range)
                return NodeState.Failure;

            return skillRuntime.TryUseWithWeaponPresentation(
                enemyData.NormalAttack,
                targetObject,
                targetObject.transform.position,
                enemyData.BasicAttackWeaponSwing,
                null,
                attackPotential)
                ? NodeState.Success
                : NodeState.Failure;
        }

        private ConditionalEnemySkill SelectAttack(
            EnemyData data,
            GameObject attacker,
            Transform target,
            GameObject targetObject,
            ISkillRuntime runtime,
            int attackPotential)
        {
            ConditionalEnemySkill selected = null;
            float distanceSquared = ((Vector2)(attacker.transform.position -
                                                 target.position)).sqrMagnitude;
            foreach (ConditionalEnemySkill candidate in
                     data.conditionalSkills ?? Array.Empty<ConditionalEnemySkill>())
            {
                if (candidate?.skill == null ||
                    selected != null && candidate.priority <= selected.priority)
                    continue;

                float minimum = Mathf.Max(0f, candidate.minimumRange);
                float maximum = candidate.maximumRange > 0f
                    ? Mathf.Max(minimum, candidate.maximumRange)
                    : Mathf.Max(minimum, maximumRange);
                if (distanceSquared < minimum * minimum ||
                    distanceSquared > maximum * maximum ||
                    !EvaluateConditionalSkills.Evaluate(
                        candidate.condition, Blackboard) ||
                    !runtime.CanUse(
                        candidate.skill,
                        targetObject,
                        target.position,
                        attackPotential,
                        candidate.projectile))
                    continue;

                selected = candidate;
            }
            return selected;
        }

        private bool TryResolveTarget(
            AiKeys.Key key,
            GameObject attacker,
            out Transform target,
            out GameObject targetObject)
        {
            target = null;
            targetObject = null;
            if (!Blackboard.TryGetValue(AiKeys.Resolve(key), out object value))
                return false;
            target = ResolveTransform(value);
            if (target == null)
                return false;
            IDamageable damageable = target.GetComponentInParent<IDamageable>() ??
                                     target.GetComponentInChildren<IDamageable>();
            if (damageable == null)
                return false;
            targetObject = damageable is Component component
                ? component.gameObject
                : target.gameObject;
            return true;
        }

        internal bool TryResolveAttack(
            AiKeys.Key key,
            float range,
            out GameObject attacker,
            out IDamageable damageable,
            out GameObject targetObject)
        {
            attacker = Blackboard.GetOrDefault(AiKeys.Self);
            damageable = null;
            targetObject = null;
            if (attacker == null ||
                !Blackboard.TryGetValue(
                    AiKeys.Resolve(key),
                    out object targetValue))
            {
                return false;
            }

            Transform target = ResolveTransform(targetValue);
            if (target == null ||
                (attacker.transform.position - target.position).sqrMagnitude >
                Mathf.Max(0f, range) * Mathf.Max(0f, range))
            {
                return false;
            }

            damageable = target.GetComponentInParent<IDamageable>() ??
                         target.GetComponentInChildren<IDamageable>();
            if (damageable == null)
                return false;

            targetObject = damageable is Component component
                ? component.gameObject
                : target.gameObject;
            return true;
        }

        internal static Transform ResolveTransform(object value)
        {
            return value switch
            {
                Transform transform when transform != null => transform,
                GameObject gameObject when gameObject != null =>
                    gameObject.transform,
                Component component when component != null =>
                    component.transform,
                _ => null
            };
        }
    }
}
