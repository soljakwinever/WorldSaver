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
    /// <summary>
    /// Takes a reachable wandering step biased toward a blackboard target.
    /// Repeated evaluations create loose, non-uniform group movement instead
    /// of making every agent follow the same direct line.
    /// </summary>
    [Serializable, AiNode(Name = "Wander Toward Target", Group = "Actions")]
    public sealed class WanderTowardTarget : AiNode
    {
        [SerializeField, InputPort("Target")]
        private AiKeys.Key targetKey = AiKeys.Key.Target;

        [SerializeField, Min(0.5f), InputPort("Step Radius")]
        private float stepRadius = 5f;

        [SerializeField, Range(0f, 1f), InputPort("Target Bias")]
        private float targetBias = 0.75f;

        [SerializeField, Min(0f), InputPort("Movement Speed")]
        private float movementSpeed = 2f;

        [SerializeField, Min(0.001f), InputPort("Arrival Tolerance")]
        private float arrivalTolerance = 0.08f;

        [SerializeField, Min(1), InputPort("Candidate Attempts")]
        private int candidateAttempts = 12;

        [SerializeField, Min(1), InputPort("Maximum Visited Tiles")]
        private int maximumVisitedTiles = 4096;

        [NonSerialized] private readonly List<Vector2Int> path = new();
        [NonSerialized] private Transform self;
        [NonSerialized] private IPathFindingMap map;
        [NonSerialized] private IPathFindingService pathFinder;
        [NonSerialized] private int waypointIndex;
        [NonSerialized] private bool hasPath;
        [NonSerialized] private int attemptsRemaining;
        [NonSerialized] private Task<List<Vector2Int>> pendingPath;
        [NonSerialized] private CancellationTokenSource pathCancellation;
        [NonSerialized] private Vector2Int pendingDestination;
        [NonSerialized] private PathFindingQuery pathQuery;

        protected override void OnEnter()
        {
            path.Clear();
            waypointIndex = 0;
            hasPath = false;
            CancelPendingPath();
            attemptsRemaining = Mathf.Max(1, candidateAttempts);

            if (!Blackboard.TryGet(AiKeys.Self, out GameObject owner) ||
                owner == null ||
                !Blackboard.TryGet(AiKeys.PathFindingMap, out map) ||
                map == null ||
                !Blackboard.TryGet(
                    AiKeys.PathFindingService,
                    out pathFinder) ||
                pathFinder == null)
            {
                return;
            }

            self = owner.transform;
            pathQuery = AiPathingUtility.CaptureQuery(owner);
            StartNextPathRequest();
        }

        protected override NodeState OnTick()
        {
            if (!hasPath)
            {
                if (pendingPath == null)
                    return NodeState.Failure;
                if (!pendingPath.IsCompleted)
                    return NodeState.Running;

                if (!ConsumePendingPath())
                {
                    if (!StartNextPathRequest())
                        return NodeState.Failure;
                    return NodeState.Running;
                }
            }

            if (!hasPath || self == null)
                return NodeState.Failure;

            float tolerance = Mathf.Max(0.001f, arrivalTolerance);
            float toleranceSquared = tolerance * tolerance;
            SkipReachedWaypoints(toleranceSquared);
            if (waypointIndex >= path.Count)
                return NodeState.Success;

            Vector3 waypoint =
                CellCenter(path[waypointIndex], self.position.z);
            if (!AiPathingUtility.PrepareCell(
                    map,
                    path[waypointIndex],
                    pathQuery))
                return NodeState.Failure;
            if (self.GetComponentInChildren<IMovementLock>()?.IsMovementLocked == true)
                return NodeState.Running;
            self.position = Vector3.MoveTowards(
                self.position,
                waypoint,
                Mathf.Max(0f, movementSpeed) *
                AiPathingUtility.GetSpeedMultiplier(
                    map,
                    path[waypointIndex],
                    pathQuery) *
                Time.deltaTime);

            SkipReachedWaypoints(toleranceSquared);
            return waypointIndex >= path.Count
                ? NodeState.Success
                : NodeState.Running;
        }

        protected override void OnAbort()
        {
            CancelPendingPath();
            path.Clear();
            hasPath = false;
        }

        protected override void OnExit()
        {
            CancelPendingPath();
        }

        private bool StartNextPathRequest()
        {
            CancelPendingPath();
            if (self == null ||
                map == null ||
                pathFinder == null ||
                !SpatialSensorUtility.TryGetPosition(
                    Blackboard,
                    targetKey,
                    out Vector3 targetPosition))
            {
                return false;
            }

            Vector2 origin = self.position;
            Vector2 towardTarget = (Vector2)targetPosition - origin;
            if (towardTarget.sqrMagnitude > 0.0001f)
                towardTarget.Normalize();

            Vector2Int start = Vector2Int.FloorToInt(origin);
            float radius = Mathf.Max(0.5f, stepRadius);
            float bias = Mathf.Clamp01(targetBias);

            while (attemptsRemaining-- > 0)
            {
                Vector2 randomDirection = UnityEngine.Random.insideUnitCircle;
                if (randomDirection.sqrMagnitude < 0.0001f)
                    randomDirection = Vector2.right;
                randomDirection.Normalize();

                Vector2 direction = Vector2.Lerp(
                    randomDirection,
                    towardTarget.sqrMagnitude > 0f
                        ? towardTarget
                        : randomDirection,
                    bias);
                if (direction.sqrMagnitude < 0.0001f)
                    continue;
                direction.Normalize();

                float distance = UnityEngine.Random.Range(
                    radius * 0.5f,
                    radius);
                Vector2Int destination =
                    Vector2Int.FloorToInt(origin + direction * distance);
                if (destination == start || !map.IsWalkable(destination))
                    continue;

                pathCancellation = new CancellationTokenSource();
                pendingDestination = destination;
                pendingPath = AiPathingUtility.FindPathAsync(
                    pathFinder,
                    start,
                    destination,
                    pathQuery,
                    Mathf.Max(1, maximumVisitedTiles),
                    pathCancellation.Token);
                return true;
            }

            return false;
        }

        private bool ConsumePendingPath()
        {
            Task<List<Vector2Int>> completed = pendingPath;
            Vector2Int destination = pendingDestination;
            pendingPath = null;
            pathCancellation?.Dispose();
            pathCancellation = null;

            if (completed.IsFaulted)
            {
                _ = completed.Exception;
                return false;
            }
            if (completed.IsCanceled)
                return false;

            List<Vector2Int> result = completed.Result;
            if (result == null || result.Count == 0)
                return false;

            path.Clear();
            path.AddRange(result);
            waypointIndex = path.Count > 1 ? 1 : 0;
            hasPath = true;
            Blackboard.Set(
                AiKeys.Destination,
                CellCenter(destination, self.position.z));
            return true;
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
                {
                    break;
                }

                waypointIndex++;
            }
        }

        private static Vector3 CellCenter(Vector2Int cell, float z)
        {
            return new Vector3(cell.x + 0.5f, cell.y + 0.5f, z);
        }
    }
}
