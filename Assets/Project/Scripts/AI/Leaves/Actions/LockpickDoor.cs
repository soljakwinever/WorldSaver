using System;
using Project.Scripts.AI.GraphEditor;
using Project.Scripts.AI.Leaves.Sensors;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Actions
{
    [Serializable, AiNode(Name = "Lockpick Door", Group = "Actions")]
    public sealed class LockpickDoor : AiNode
    {
        [SerializeField, InputPort("Door Position")]
        private AiKeys.Key targetKey = AiKeys.Key.Target;

        [SerializeField, Min(1), InputPort("Skill")]
        private int skill = 1;

        [SerializeField, Min(0f), InputPort("Maximum Range")]
        private float maximumRange = 1.5f;

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

            return handler.TryLockpickCell(
                Vector2Int.FloorToInt(target),
                Mathf.Max(1, skill))
                ? NodeState.Success
                : NodeState.Failure;
        }
    }
}
