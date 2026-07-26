using System;
using Project.Scripts.AI.GraphEditor;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Sensors
{
    [Serializable, AiNode(Name = "Is On Valid Tile", Group = "Sensors")]
    public sealed class IsOnValidTile : AiNode
    {
        [SerializeField, InputPort("Position")]
        private AiKeys.Key positionKey = AiKeys.Key.Self;

        protected override NodeState OnTick()
        {
            if (!Blackboard.TryGet(
                    AiKeys.PathFindingMap,
                    out IPathFindingMap map) ||
                map == null ||
                !SpatialSensorUtility.TryGetPosition(
                    Blackboard, positionKey, out Vector3 position))
                return NodeState.Failure;

            return map.IsWalkable(Vector2Int.FloorToInt(position))
                ? NodeState.Success
                : NodeState.Failure;
        }
    }
}
