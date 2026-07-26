using System;
using System.Collections.Generic;
using Project.Scripts.AI.GraphEditor;
using Project.Scripts.AI.Leaves.Sensors;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Actions
{
    [Serializable, AiNode(Name = "Move To Target", Group = "Actions")]
    public sealed class MoveToTarget : AiNode
    {
        [SerializeField, InputPort("Target")]
        private AiKeys.Key targetKey = AiKeys.Key.Target;

        [SerializeField, Min(0f), InputPort("Movement Speed")]
        private float movementSpeed = 2f;

        [SerializeField, Min(0.001f), InputPort("Arrival Tolerance")]
        private float arrivalTolerance = 0.08f;

        [SerializeField, Min(0f), InputPort("Repath Interval")]
        private float repathInterval = 0.25f;

        [SerializeField, Min(1), InputPort("Maximum Visited Tiles")]
        private int maximumVisitedTiles = 4096;

        [NonSerialized]
        private readonly List<Vector2Int> path = new();

        [NonSerialized]
        private Transform self;

        [NonSerialized]
        private IPathFindingService pathFinder;

        [NonSerialized]
        private int waypointIndex;

        [NonSerialized]
        private Vector2Int plannedDestination;

        [NonSerialized]
        private float nextRepathTime;

        protected override void OnEnter()
        {
            path.Clear();
            waypointIndex = 0;
            nextRepathTime = 0f;

            self = Blackboard.TryGet(AiKeys.Self, out GameObject owner) &&
                   owner != null
                ? owner.transform
                : null;
            Blackboard.TryGet(AiKeys.PathFindingService, out pathFinder);
        }

        protected override NodeState OnTick()
        {
            if (self == null || pathFinder == null ||
                !SpatialSensorUtility.TryGetPosition(
                    Blackboard,
                    targetKey,
                    out Vector3 targetPosition))
                return NodeState.Failure;

            float tolerance = Mathf.Max(0.001f, arrivalTolerance);
            if ((self.position - targetPosition).sqrMagnitude <=
                tolerance * tolerance)
                return NodeState.Success;

            Vector2Int targetCell = Vector2Int.FloorToInt(targetPosition);
            bool destinationChanged = path.Count == 0 ||
                                      targetCell != plannedDestination;
            if (destinationChanged || Time.time >= nextRepathTime)
            {
                if (!TryPlanPath(targetCell))
                    return NodeState.Failure;

                nextRepathTime = Time.time + Mathf.Max(0f, repathInterval);
            }

            SkipReachedWaypoints(tolerance * tolerance);
            if (waypointIndex >= path.Count)
                return NodeState.Success;

            Vector3 waypoint = CellCenter(
                path[waypointIndex],
                self.position.z);
            self.position = Vector3.MoveTowards(
                self.position,
                waypoint,
                Mathf.Max(0f, movementSpeed) * Time.deltaTime);
            SkipReachedWaypoints(tolerance * tolerance);

            return NodeState.Running;
        }

        protected override void OnAbort()
        {
            path.Clear();
            waypointIndex = 0;
        }

        private bool TryPlanPath(Vector2Int destination)
        {
            path.Clear();
            Vector2Int start = Vector2Int.FloorToInt(self.position);
            if (!pathFinder.TryFindPath(
                    start,
                    destination,
                    path,
                    Mathf.Max(1, maximumVisitedTiles)))
                return false;

            plannedDestination = destination;
            waypointIndex = path.Count > 1 ? 1 : 0;
            Blackboard.Set(
                AiKeys.Destination,
                CellCenter(destination, self.position.z));
            return path.Count > 0;
        }

        private void SkipReachedWaypoints(float toleranceSquared)
        {
            while (waypointIndex < path.Count)
            {
                Vector3 waypoint =
                    CellCenter(path[waypointIndex], self.position.z);
                if ((self.position - waypoint).sqrMagnitude >
                    toleranceSquared)
                    break;

                waypointIndex++;
            }
        }

        private static Vector3 CellCenter(Vector2Int cell, float z)
        {
            return new Vector3(cell.x + 0.5f, cell.y + 0.5f, z);
        }
    }
}
