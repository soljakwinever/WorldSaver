using System;
using Project.Scripts.AI.GraphEditor;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Sensors
{
    [Serializable, AiNode(Name = "Has Line Of Sight", Group = "Sensors")]
    public sealed class HasLineOfSight : AiNode
    {
        private const int BuildingsLayer = 6;

        [SerializeField, InputPort("From")]
        private AiKeys.Key fromKey = AiKeys.Key.Self;

        [SerializeField, InputPort("To")]
        private AiKeys.Key toKey = AiKeys.Key.Target;

        [SerializeField, InputPort("Blocking Layers")]
        [Tooltip("Collision layers checked for cover. The intended target is allowed even when it shares one of these layers.")]
        private LayerMask blockingLayers =
            (1 << 0) | (1 << BuildingsLayer);

        protected override NodeState OnTick()
        {
            if (!SpatialSensorUtility.TryGetPosition(
                    Blackboard, fromKey, out Vector3 from) ||
                !SpatialSensorUtility.TryGetPosition(
                    Blackboard, toKey, out Vector3 to))
            {
                return NodeState.Failure;
            }

            SpatialSensorUtility.TryGetTransform(
                Blackboard, fromKey, out Transform attacker);
            SpatialSensorUtility.TryGetTransform(
                Blackboard, toKey, out Transform target);
            return LineOfFireUtility.HasClearPath(
                from,
                to,
                attacker,
                target,
                blockingLayers)
                ? NodeState.Success
                : NodeState.Failure;
        }
    }
}
