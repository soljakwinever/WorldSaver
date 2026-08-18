using System;
using Project.Scripts.AI.GraphEditor;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Sensors
{
    [Serializable, AiNode(Name = "Select Remembered Target Position", Group = "Sensors")]
    public sealed class SelectRememberedTargetPosition : AiNode
    {
        [SerializeField, Min(0f)] private float memoryDuration = 5f;
        [SerializeField, Min(0.01f)] private float arrivalTolerance = 0.8f;

        protected override NodeState OnTick()
        {
            GameObject self = Blackboard.GetOrDefault(AiKeys.Self);
            TargetTrackingMemory memory =
                Blackboard.GetOrDefault(AiKeys.TargetMemory);
            if (self == null || memory == null ||
                !memory.TryGetNext(
                    self.transform.position,
                    Time.time,
                    memoryDuration,
                    arrivalTolerance,
                    out Vector3 destination))
                return NodeState.Failure;

            Blackboard.Set(AiKeys.Destination, destination);
            return NodeState.Success;
        }
    }
}
