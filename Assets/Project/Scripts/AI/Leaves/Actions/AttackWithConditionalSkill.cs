using System;
using Project.Scripts.AI.GraphEditor;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using Project.Scripts.Gameplay;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Actions
{
    [Serializable, AiNode(
        Name = "Attack With Conditional Skill",
        Group = "Actions")]
    public sealed class AttackWithConditionalSkill : AiNode
    {
        [SerializeField, InputPort("Target")]
        private AiKeys.Key targetKey = AiKeys.Key.Target;

        [SerializeField, Min(0f), InputPort("Maximum Range")]
        private float maximumRange = 2f;

        [NonSerialized] private int attackSequence;

        protected override void OnEnter() => attackSequence = 0;

        protected override NodeState OnTick()
        {
            GameObject attacker = Blackboard.GetOrDefault(AiKeys.Self);
            EnemyAttackController controller = attacker != null
                ? attacker.GetComponentInParent<EnemyAttackController>()
                : null;
            if (attackSequence > 0 && controller != null)
            {
                if (controller.TryGetResult(attackSequence, out bool succeeded))
                    return succeeded ? NodeState.Success : NodeState.Failure;
                return controller.CurrentSequence == attackSequence &&
                       controller.IsAttacking
                    ? NodeState.Running
                    : NodeState.Failure;
            }

            if (attacker == null ||
                !Blackboard.TryGet(AiKeys.ConditionalSkill, out SkillData skill) ||
                skill == null ||
                !Blackboard.TryGetValue(
                    AiKeys.Resolve(targetKey), out object targetValue))
                return NodeState.Failure;

            Transform target = AttackTarget.ResolveTransform(targetValue);
            float range = Mathf.Max(0f, maximumRange);
            if (target == null ||
                (attacker.transform.position - target.position).sqrMagnitude >
                range * range)
                return NodeState.Failure;

            GameObject targetObject = target.gameObject;
            IDamageable damageable =
                target.GetComponentInParent<IDamageable>() ??
                target.GetComponentInChildren<IDamageable>();
            if (damageable is Component component)
                targetObject = component.gameObject;
            else if (damageable == null)
                return NodeState.Failure;

            if (!Blackboard.TryGet(
                    AiKeys.ConditionalAttack,
                    out ConditionalEnemySkill selected) || selected == null)
                return NodeState.Failure;

            int attackPotential = Mathf.Max(
                0, Blackboard.GetOrDefault(AiKeys.Attack));
            controller ??= attacker.AddComponent<EnemyAttackController>();
            return controller.TryBegin(
                selected,
                targetObject,
                attackPotential,
                out attackSequence)
                ? NodeState.Running
                : NodeState.Failure;
        }
    }
}
