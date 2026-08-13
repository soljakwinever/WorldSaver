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
        [Tooltip("Layers that block sight. Defaults to the Buildings layer.")]
        private LayerMask blockingLayers = 1 << BuildingsLayer;

        protected override NodeState OnTick()
        {
            if (!SpatialSensorUtility.TryGetPosition(
                    Blackboard, fromKey, out Vector3 from) ||
                !SpatialSensorUtility.TryGetPosition(
                    Blackboard, toKey, out Vector3 to))
            {
                return NodeState.Failure;
            }

            Vector2 direction = (Vector2)(to - from);
            float distance = direction.magnitude;
            if (distance <= Mathf.Epsilon)
                return NodeState.Success;

            RaycastHit2D obstruction = Physics2D.Raycast(
                from,
                direction / distance,
                distance,
                blockingLayers);

            return obstruction.collider == null
                ? NodeState.Success
                : NodeState.Failure;
        }
    }
}
