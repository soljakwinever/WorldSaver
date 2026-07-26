using System;
using Project.Scripts.AI.GraphEditor;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Sensors
{
    [Serializable, AiNode(Name = "Is At Destination", Group = "Sensors")]
    public sealed class IsAtDestination : AiNode
    {
        [SerializeField, InputPort("Position")]
        private AiKeys.Key positionKey = AiKeys.Key.Self;

        [SerializeField, InputPort("Destination")]
        private AiKeys.Key destinationKey = AiKeys.Key.Destination;

        [SerializeField, Min(0f), InputPort("Tolerance")]
        private float tolerance = 0.1f;

        protected override NodeState OnTick()
        {
            if (!SpatialSensorUtility.TryGetPosition(
                    Blackboard, positionKey, out Vector3 position) ||
                !SpatialSensorUtility.TryGetPosition(
                    Blackboard, destinationKey, out Vector3 destination))
                return NodeState.Failure;

            return (position - destination).sqrMagnitude <=
                   tolerance * tolerance
                ? NodeState.Success
                : NodeState.Failure;
        }
    }
}
