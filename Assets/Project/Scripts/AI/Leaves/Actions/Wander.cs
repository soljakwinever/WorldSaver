using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Project.Scripts.AI.GraphEditor;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Actions
{
    /// <summary>
    /// Selects a random reachable tile near Self and follows the generated
    /// tile path until it arrives.
    /// </summary>
    [Serializable, AiNode(Name = "Wander", Group = "Actions")]
    public sealed class Wander : AiNode
    {
        [SerializeField, Min(0.5f), InputPort("Radius")]
        private float radius = 5f;

        [SerializeField, Min(0f), InputPort("Movement Speed")]
        private float movementSpeed = 2f;

        [SerializeField, Min(0.001f), InputPort("Arrival Tolerance")]
        private float arrivalTolerance = 0.08f;

        [SerializeField, Min(1), InputPort("Candidate Attempts")]
        private int candidateAttempts = 12;

        [SerializeField, Min(1), InputPort("Maximum Visited Tiles")]
        private int maximumVisitedTiles = 4096;

        [NonSerialized]
        private readonly List<Vector2Int> path = new();

        [NonSerialized]
        private Transform self;

        [NonSerialized]
        private int waypointIndex;

        [NonSerialized]
        private bool hasPath;

        [NonSerialized]
        private IPathFindingMap map;

        [NonSerialized]
        private IPathFindingService pathFinder;

        [NonSerialized]
        private Task<List<Vector2Int>> pendingPath;

        [NonSerialized]
        private CancellationTokenSource pathCancellation;

        [NonSerialized]
        private Vector2Int pendingDestination;

        [NonSerialized]
        private int attemptsRemaining;
        [NonSerialized]
        private PathFindingQuery pathQuery;

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

            Vector3 target = CellCenter(path[waypointIndex], self.position.z);
            if (!AiPathingUtility.PrepareCell(
                    map,
                    path[waypointIndex],
                    pathQuery))
                return NodeState.Failure;
            if (self.GetComponentInChildren<IMovementLock>()?.IsMovementLocked == true)
                return NodeState.Running;
            float movement = Mathf.Max(0f, movementSpeed) *
                             AiPathingUtility.GetActorSpeedMultiplier(self.gameObject) *
                             AiPathingUtility.GetSpeedMultiplier(
                                 map,
                                 path[waypointIndex],
                                 pathQuery) *
                             Time.deltaTime;
            self.position = Vector3.MoveTowards(
                self.position,
                target,
                movement);

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
            if (self == null || map == null || pathFinder == null)
                return false;

            Vector2Int start = Vector2Int.FloorToInt(self.position);
            float searchRadius = Mathf.Max(0.5f, radius);

            while (attemptsRemaining-- > 0)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle *
                                 searchRadius;
                Vector2Int destination = Vector2Int.FloorToInt(
                    (Vector2)self.position + offset);

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
