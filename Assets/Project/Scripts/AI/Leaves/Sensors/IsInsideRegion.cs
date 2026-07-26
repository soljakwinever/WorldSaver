using System;
using Project.Scripts.AI.GraphEditor;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Sensors
{
    [Serializable, AiNode(Name = "Is Inside Region", Group = "Sensors")]
    public sealed class IsInsideRegion : AiNode
    {
        [SerializeField, InputPort("Position")]
        private AiKeys.Key positionKey = AiKeys.Key.Self;

        [SerializeField, InputPort("Region")]
        private Rect region;

        protected override NodeState OnTick()
        {
            if (!SpatialSensorUtility.TryGetPosition(
                    Blackboard, positionKey, out Vector3 position))
                return NodeState.Failure;

            return region.Contains(position)
                ? NodeState.Success
                : NodeState.Failure;
        }
    }
}
