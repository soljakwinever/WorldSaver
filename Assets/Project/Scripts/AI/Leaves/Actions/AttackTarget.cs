using System;
using Project.Scripts.AI.GraphEditor;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using Project.Scripts.Gameplay;
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

        protected override NodeState OnTick()
        {
            if (!TryResolveAttack(
                    targetKey,
                    maximumRange,
                    out GameObject attacker,
                    out _,
                    out GameObject targetObject) ||
                !Blackboard.TryGet(
                    AiKeys.AttackService,
                    out IAttackService attackService) ||
                attackService == null)
            {
                return NodeState.Failure;
            }

            if (!Blackboard.TryGet(AiKeys.EnemyData, out EnemyData enemyData) ||
                enemyData?.NormalAttack == null)
                return NodeState.Failure;

            int attackPotential = Mathf.Max(
                0,
                Blackboard.GetOrDefault(AiKeys.Attack));

            ISkillRuntime skillRuntime =
                attacker.GetComponentInParent<ISkillRuntime>();
            if (skillRuntime == null)
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
