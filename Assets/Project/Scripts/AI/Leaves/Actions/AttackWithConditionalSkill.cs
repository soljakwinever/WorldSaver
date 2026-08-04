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

        protected override NodeState OnTick()
        {
            GameObject attacker = Blackboard.GetOrDefault(AiKeys.Self);
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

            ISkillRuntime runtime =
                attacker.GetComponentInParent<ISkillRuntime>();
            if (runtime == null)
                return NodeState.Failure;

            int attackPotential = Mathf.Max(
                0, Blackboard.GetOrDefault(AiKeys.Attack));
            Blackboard.TryGet(
                AiKeys.ConditionalProjectile,
                out ProjectileData projectile);
            Blackboard.TryGet(
                AiKeys.ConditionalWeaponSwing,
                out WeaponSwingAnimation weaponSwing);
            return runtime.TryUseWithWeaponPresentation(
                skill,
                targetObject,
                target.position,
                weaponSwing,
                null,
                attackPotential,
                projectile)
                ? NodeState.Success
                : NodeState.Failure;
        }
    }
}
