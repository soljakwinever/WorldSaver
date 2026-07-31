using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
        [NonSerialized] private IPathFindingMap map;
        [NonSerialized] private PathFindingQuery pathQuery;

        [NonSerialized]
        private int waypointIndex;

        [NonSerialized]
        private Vector2Int plannedDestination;

        [NonSerialized]
        private float nextRepathTime;

        [NonSerialized]
        private Task<List<Vector2Int>> pendingPath;

        [NonSerialized]
        private CancellationTokenSource pathCancellation;

        [NonSerialized]
        private Vector2Int pendingDestination;

        protected override void OnEnter()
        {
            path.Clear();
            waypointIndex = 0;
            nextRepathTime = 0f;
            CancelPendingPath();

            self = Blackboard.TryGet(AiKeys.Self, out GameObject owner) &&
                   owner != null
                ? owner.transform
                : null;
            Blackboard.TryGet(AiKeys.PathFindingService, out pathFinder);
            Blackboard.TryGet(AiKeys.PathFindingMap, out map);
            pathQuery = AiPathingUtility.CaptureQuery(owner);
        }

        protected override NodeState OnTick()
        {
            if (self == null || pathFinder == null ||
                !SpatialSensorUtility.TryGetPosition(
                    Blackboard,
                    targetKey,
                    out Vector3 targetPosition))
                return NodeState.Failure;

            PendingPathState pendingState = ConsumePendingPath();
            if (pendingState == PendingPathState.Failed)
                return NodeState.Failure;

            float tolerance = Mathf.Max(0.001f, arrivalTolerance);
            if ((self.position - targetPosition).sqrMagnitude <=
                tolerance * tolerance)
                return NodeState.Success;

            Vector2Int targetCell = Vector2Int.FloorToInt(targetPosition);
            bool destinationChanged = path.Count == 0 ||
                                      targetCell != plannedDestination;
            bool pendingDestinationChanged =
                pendingPath != null && targetCell != pendingDestination;
            bool pathExpired = Time.time >= nextRepathTime;
            if (pendingDestinationChanged ||
                pendingPath == null && (destinationChanged || pathExpired))
            {
                // Continuation frames are for cheap movement only. Defer A* to
                // a full tree pass so pathfinding cannot unexpectedly dominate
                // every-frame AI work.
                if (IsContinuationPass)
                {
                    RequestEvaluation();
                }
                else
                {
                    StartPathRequest(targetCell);
                }
            }

            if (path.Count == 0)
                return pendingPath != null
                    ? NodeState.Running
                    : NodeState.Failure;

            SkipReachedWaypoints(tolerance * tolerance);
            if (waypointIndex >= path.Count)
                return NodeState.Success;

            Vector3 waypoint = CellCenter(
                path[waypointIndex],
                self.position.z);
            if (!AiPathingUtility.PrepareCell(
                    map,
                    path[waypointIndex],
                    pathQuery))
                return NodeState.Failure;
            self.position = Vector3.MoveTowards(
                self.position,
                waypoint,
                Mathf.Max(0f, movementSpeed) *
                AiPathingUtility.GetSpeedMultiplier(
                    map,
                    path[waypointIndex],
                    pathQuery) *
                Time.deltaTime);
            SkipReachedWaypoints(tolerance * tolerance);

            return NodeState.Running;
        }

        protected override void OnAbort()
        {
            CancelPendingPath();
            path.Clear();
            waypointIndex = 0;
        }

        protected override void OnExit()
        {
            CancelPendingPath();
        }

        private void StartPathRequest(Vector2Int destination)
        {
            CancelPendingPath();
            Vector2Int start = Vector2Int.FloorToInt(self.position);
            pathCancellation = new CancellationTokenSource();
            pendingDestination = destination;
            pendingPath = AiPathingUtility.FindPathAsync(
                pathFinder,
                start,
                destination,
                pathQuery,
                Mathf.Max(1, maximumVisitedTiles),
                pathCancellation.Token);
        }

        private PendingPathState ConsumePendingPath()
        {
            if (pendingPath == null || !pendingPath.IsCompleted)
                return PendingPathState.Waiting;

            Task<List<Vector2Int>> completed = pendingPath;
            Vector2Int destination = pendingDestination;
            pendingPath = null;
            pathCancellation?.Dispose();
            pathCancellation = null;

            if (completed.IsFaulted)
            {
                _ = completed.Exception;
                return PendingPathState.Failed;
            }
            if (completed.IsCanceled)
                return PendingPathState.Failed;

            List<Vector2Int> result = completed.Result;
            if (result == null || result.Count == 0)
                return PendingPathState.Failed;

            path.Clear();
            path.AddRange(result);
            plannedDestination = destination;
            waypointIndex = path.Count > 1 ? 1 : 0;
            Blackboard.Set(
                AiKeys.Destination,
                CellCenter(destination, self.position.z));

            float interval = Mathf.Max(0f, repathInterval);
            nextRepathTime = Time.time + interval;
            if (interval > 0f)
                RequestEvaluation(interval);
            return PendingPathState.Ready;
        }

        private void CancelPendingPath()
        {
            if (pathCancellation != null)
            {
                pathCancellation.Cancel();
                pathCancellation.Dispose();
                pathCancellation = null;
            }
            pendingPath = null;
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

        private enum PendingPathState
        {
            Waiting,
            Ready,
            Failed
        }
    }
}
