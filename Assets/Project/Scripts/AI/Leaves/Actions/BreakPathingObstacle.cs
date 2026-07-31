using System;
using Project.Scripts.AI.GraphEditor;
using Project.Scripts.AI.Leaves.Sensors;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Actions
{
    [Serializable, AiNode(
        Name = "Break Pathing Obstacle",
        Group = "Actions")]
    public sealed class BreakPathingObstacle : AiNode
    {
        [SerializeField, InputPort("Obstacle Position")]
        private AiKeys.Key targetKey = AiKeys.Key.Target;

        [SerializeField, Min(1), InputPort("Damage")]
        private int damage = 1;

        [SerializeField, Min(0f), InputPort("Maximum Range")]
        private float maximumRange = 1.5f;

        [SerializeField, Min(0f), InputPort("Attack Interval")]
        private float attackInterval = 0.5f;

        [NonSerialized] private float nextAttackTime;

        protected override void OnEnter()
        {
            nextAttackTime = 0f;
        }

        protected override NodeState OnTick()
        {
            if (!Blackboard.TryGet(AiKeys.Self, out GameObject self) ||
                self == null ||
                !Blackboard.TryGet(
                    AiKeys.PathFindingMap,
                    out IPathFindingMap map) ||
                map is not IPathTraversalHandler handler ||
                !SpatialSensorUtility.TryGetPosition(
                    Blackboard,
                    targetKey,
                    out Vector3 target))
                return NodeState.Failure;

            float range = Mathf.Max(0f, maximumRange);
            if ((self.transform.position - target).sqrMagnitude >
                range * range)
                return NodeState.Failure;

            Vector2Int targetCell = Vector2Int.FloorToInt(target);
            if (!handler.IsBreachableCell(targetCell))
                return NodeState.Success;

            float remaining = nextAttackTime - Time.time;
            if (remaining > 0f)
            {
                RequestEvaluation(remaining);
                return NodeState.Running;
            }

            bool destroyed = handler.TryBreachCell(
                targetCell,
                Mathf.Max(1, damage));
            if (destroyed)
                return NodeState.Success;

            nextAttackTime =
                Time.time + Mathf.Max(0f, attackInterval);
            if (attackInterval > 0f)
                RequestEvaluation(attackInterval);
            return NodeState.Running;
        }
    }
}
