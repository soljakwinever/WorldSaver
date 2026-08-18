using System;
using Project.Scripts.AI.GraphEditor;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Sensors
{
    [Serializable, AiNode(Name = "Is Within Distance", Group = "Sensors")]
    public sealed class IsWithinDistance : AiNode
    {
        [SerializeField, InputPort("From")]
        private AiKeys.Key fromKey = AiKeys.Key.Self;

        [SerializeField, InputPort("To")]
        private AiKeys.Key toKey = AiKeys.Key.Target;

        [SerializeField, Min(0f), InputPort("Distance")]
        [Tooltip("Zero derives the engagement distance from the enemy's configured attack patterns.")]
        private float distance = 1f;

        protected override NodeState OnTick()
        {
            if (!SpatialSensorUtility.TryGetPosition(
                    Blackboard, fromKey, out Vector3 from) ||
                !SpatialSensorUtility.TryGetPosition(
                    Blackboard, toKey, out Vector3 to))
                return NodeState.Failure;

            float effectiveDistance = ResolveDistance();
            return effectiveDistance > 0f &&
                   (from - to).sqrMagnitude <=
                   effectiveDistance * effectiveDistance
                ? NodeState.Success
                : NodeState.Failure;
        }

        private float ResolveDistance()
        {
            if (distance > 0f)
                return distance;

            if (!Blackboard.TryGet(AiKeys.EnemyData, out EnemyData enemy) ||
                enemy?.conditionalSkills == null)
                return 0f;

            float maximum = 0f;
            foreach (ConditionalEnemySkill attack in enemy.conditionalSkills)
            {
                if (attack?.skill == null)
                    continue;
                maximum = Mathf.Max(maximum, attack.maximumRange);
            }
            return maximum;
        }
    }
}
