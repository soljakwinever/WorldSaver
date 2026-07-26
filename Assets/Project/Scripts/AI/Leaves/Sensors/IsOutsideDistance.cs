using System;
using Project.Scripts.AI.GraphEditor;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Sensors
{
    [Serializable, AiNode(Name = "Is Outside Distance", Group = "Sensors")]
    public sealed class IsOutsideDistance : AiNode
    {
        [SerializeField, InputPort("From")]
        private AiKeys.Key fromKey = AiKeys.Key.Self;

        [SerializeField, InputPort("To")]
        private AiKeys.Key toKey = AiKeys.Key.Target;

        [SerializeField, Min(0f), InputPort("Distance")]
        private float distance = 1f;

        protected override NodeState OnTick()
        {
            if (!SpatialSensorUtility.TryGetPosition(
                    Blackboard, fromKey, out Vector3 from) ||
                !SpatialSensorUtility.TryGetPosition(
                    Blackboard, toKey, out Vector3 to))
                return NodeState.Failure;

            return (from - to).sqrMagnitude > distance * distance
                ? NodeState.Success
                : NodeState.Failure;
        }
    }
}
