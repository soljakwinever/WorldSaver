using System;
using Project.Scripts.AI.GraphEditor;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Sensors
{
    [Serializable, AiNode(Name = "Record Visible Target Path", Group = "Sensors")]
    public sealed class RecordVisibleTargetPath : AiNode
    {
        [SerializeField, Min(0.01f)] private float minimumSpacing = 0.4f;
        [SerializeField, Min(1)] private int maximumBreadcrumbs = 12;

        protected override NodeState OnTick()
        {
            GameObject self = Blackboard.GetOrDefault(AiKeys.Self);
            Transform target = Blackboard.GetOrDefault(AiKeys.Target);
            TargetTrackingMemory memory =
                Blackboard.GetOrDefault(AiKeys.TargetMemory);
            if (self == null || target == null || memory == null)
                return NodeState.Failure;

            memory.Record(
                target.position,
                self.transform.position,
                Time.time,
                minimumSpacing,
                maximumBreadcrumbs);
            return NodeState.Success;
        }
    }
}
