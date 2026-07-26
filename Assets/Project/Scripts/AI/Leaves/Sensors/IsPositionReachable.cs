using System;
using System.Collections.Generic;
using Project.Scripts.AI.GraphEditor;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Sensors
{
    [Serializable, AiNode(Name = "Is Position Reachable", Group = "Sensors")]
    public sealed class IsPositionReachable : AiNode
    {
        [SerializeField, InputPort("From")]
        private AiKeys.Key fromKey = AiKeys.Key.Self;

        [SerializeField, InputPort("Position")]
        private AiKeys.Key positionKey = AiKeys.Key.Destination;

        [SerializeField, Min(1), InputPort("Maximum Visited Tiles")]
        private int maximumVisitedTiles = 4096;

        [NonSerialized]
        private readonly List<Vector2Int> path = new();

        protected override NodeState OnTick()
        {
            if (!Blackboard.TryGet(
                    AiKeys.PathFindingService,
                    out IPathFindingService pathFinder) ||
                pathFinder == null ||
                !SpatialSensorUtility.TryGetPosition(
                    Blackboard, fromKey, out Vector3 from) ||
                !SpatialSensorUtility.TryGetPosition(
                    Blackboard, positionKey, out Vector3 destination))
                return NodeState.Failure;

            bool reachable = pathFinder.TryFindPath(
                Vector2Int.FloorToInt(from),
                Vector2Int.FloorToInt(destination),
                path,
                Mathf.Max(1, maximumVisitedTiles));
            return reachable ? NodeState.Success : NodeState.Failure;
        }
    }
}
