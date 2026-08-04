using System;
using Project.Scripts.AI.GraphEditor;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Sensors
{
    [Serializable, AiNode(
        Name = "Evaluate Conditional Skills",
        Group = "Sensors")]
    public sealed class EvaluateConditionalSkills : AiNode
    {
        private const int MaximumCompositionDepth = 16;

        protected override NodeState OnTick()
        {
            Blackboard.Set<SkillData>(AiKeys.ConditionalSkill, null);
            Blackboard.Set<ProjectileData>(AiKeys.ConditionalProjectile, null);
            Blackboard.Set<WeaponSwingAnimation>(
                AiKeys.ConditionalWeaponSwing,
                null);
            if (!Blackboard.TryGet(AiKeys.EnemyData, out EnemyData enemy) ||
                enemy?.conditionalSkills == null)
                return NodeState.Failure;

            foreach (ConditionalEnemySkill candidate in enemy.conditionalSkills)
            {
                if (candidate?.skill == null ||
                    !Evaluate(candidate.condition, Blackboard, 0))
                    continue;

                Blackboard.Set(AiKeys.ConditionalSkill, candidate.skill);
                Blackboard.Set(
                    AiKeys.ConditionalProjectile,
                    candidate.projectile);
                Blackboard.Set(
                    AiKeys.ConditionalWeaponSwing,
                    candidate.weaponSwing != null
                        ? candidate.weaponSwing
                        : enemy.BasicAttackWeaponSwing);
                return NodeState.Success;
            }

            return NodeState.Failure;
        }

        internal static bool Evaluate(
            EnemySkillCondition condition,
            Blackboard blackboard,
            int depth = 0)
        {
            if (condition == null || blackboard == null ||
                depth >= MaximumCompositionDepth)
                return false;

            EnemySkillCondition[] children =
                condition.conditions ?? Array.Empty<EnemySkillCondition>();
            switch (condition.type)
            {
                case EnemySkillConditionType.Always:
                    return true;
                case EnemySkillConditionType.DistanceToTarget:
                    if (!blackboard.TryGet(AiKeys.Self, out GameObject self) ||
                        self == null ||
                        !blackboard.TryGet(AiKeys.Target, out Transform target) ||
                        target == null)
                        return false;
                    float distance = Mathf.Max(0f, condition.distance);
                    return ((Vector2)(self.transform.position - target.position))
                        .sqrMagnitude <= distance * distance;
                case EnemySkillConditionType.All:
                    if (children.Length == 0)
                        return false;
                    foreach (EnemySkillCondition child in children)
                        if (!Evaluate(child, blackboard, depth + 1))
                            return false;
                    return true;
                case EnemySkillConditionType.Any:
                    foreach (EnemySkillCondition child in children)
                        if (Evaluate(child, blackboard, depth + 1))
                            return true;
                    return false;
                case EnemySkillConditionType.Not:
                    return children.Length == 1 &&
                           !Evaluate(children[0], blackboard, depth + 1);
                default:
                    return false;
            }
        }
    }
}
