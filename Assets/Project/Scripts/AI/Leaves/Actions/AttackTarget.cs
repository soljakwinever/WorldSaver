using System;
using Project.Scripts.AI.GraphEditor;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Actions
{
    [Serializable, AiNode(Name = "Attack Target", Group = "Actions")]
    public sealed class AttackTarget : AiNode
    {
        [SerializeField, InputPort("Target")]
        private AiKeys.Key targetKey = AiKeys.Key.Target;

        [SerializeField, Min(0), InputPort("Force")]
        private int force = 1;

        [SerializeField, InputPort("Weapon")]
        private ToolData weapon;

        [SerializeField, Min(0f), InputPort("Maximum Range")]
        private float maximumRange = 1f;

        protected override NodeState OnTick()
        {
            if (!TryResolveAttack(
                    targetKey,
                    maximumRange,
                    out GameObject attacker,
                    out IDamageable target) ||
                !Blackboard.TryGet(
                    AiKeys.AttackService,
                    out IAttackService attackService) ||
                attackService == null)
            {
                return NodeState.Failure;
            }

            attackService.Attack(
                target,
                new AttackContext(
                    attacker,
                    weapon,
                    Mathf.Max(0, force),
                    EntityDamageSource.Enemy,
                    attacker.GetComponentInParent<IEntityDamageSource>()?
                        .DamageTags));
            return NodeState.Success;
        }

        internal bool TryResolveAttack(
            AiKeys.Key key,
            float range,
            out GameObject attacker,
            out IDamageable damageable)
        {
            attacker = Blackboard.GetOrDefault(AiKeys.Self);
            damageable = null;
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
            return damageable != null;
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
