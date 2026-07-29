using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Project.Scripts.AI.GraphEditor;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Actions
{
    [Serializable, AiNode(Name = "Flee From Target", Group = "Actions")]
    public sealed class FleeFromTarget : AiNode
    {
        [SerializeField, InputPort("Target")]
        private AiKeys.Key targetKey = AiKeys.Key.Target;

        [SerializeField, Min(0f), InputPort("Safe Distance")]
        private float safeDistance = 6f;

        [SerializeField, Min(0f), InputPort("Movement Speed")]
        private float movementSpeed = 3f;

        [SerializeField, Min(0.001f), InputPort("Arrival Tolerance")]
        private float arrivalTolerance = 0.08f;

        [SerializeField, Min(0f), InputPort("Repath Interval")]
        private float repathInterval = 0.25f;

        [SerializeField, Min(1), InputPort("Candidate Attempts")]
        private int candidateAttempts = 8;

        [SerializeField, Min(1), InputPort("Maximum Visited Tiles")]
        private int maximumVisitedTiles = 4096;

        [NonSerialized] private readonly List<Vector2Int> path = new();
        [NonSerialized] private Transform self;
        [NonSerialized] private IPathFindingMap map;
        [NonSerialized] private IPathFindingService pathFinder;
        [NonSerialized] private int waypointIndex;
        [NonSerialized] private int attemptsRemaining;
        [NonSerialized] private float nextRepathTime;
        [NonSerialized] private Vector3 lastThreatPosition;
        [NonSerialized] private Task<List<Vector2Int>> pendingPath;
        [NonSerialized] private CancellationTokenSource pathCancellation;

        protected override void OnEnter()
        {
            path.Clear();
            waypointIndex = 0;
            attemptsRemaining = Mathf.Max(1, candidateAttempts);
            nextRepathTime = 0f;
            CancelPendingPath();

            self = Blackboard.TryGet(AiKeys.Self, out GameObject owner) &&
                   owner != null
                ? owner.transform
                : null;
            Blackboard.TryGet(AiKeys.PathFindingMap, out map);
            Blackboard.TryGet(AiKeys.PathFindingService, out pathFinder);

            if (self != null && map != null && pathFinder != null &&
                TryGetThreatPosition(out Vector3 threatPosition))
            {
                lastThreatPosition = threatPosition;
                StartNextPathRequest();
            }
        }

        protected override NodeState OnTick()
        {
            if (self == null || map == null || pathFinder == null ||
                !TryGetThreatPosition(out Vector3 threatPosition))
            {
                return NodeState.Failure;
            }

            lastThreatPosition = threatPosition;
            float requiredDistance = Mathf.Max(0f, safeDistance);
            if ((self.position - threatPosition).sqrMagnitude >=
                requiredDistance * requiredDistance)
            {
                return NodeState.Success;
            }

            if (pendingPath != null && pendingPath.IsCompleted)
            {
                if (IsContinuationPass)
                {
                    RequestEvaluation();
                    return NodeState.Running;
                }

                if (!ConsumePendingPath())
                {
                    if (!StartNextPathRequest())
                        return NodeState.Failure;
                }
            }

            if (pendingPath == null &&
                (path.Count == 0 || Time.time >= nextRepathTime))
            {
                if (IsContinuationPass)
                {
                    RequestEvaluation();
                }
                else
                {
                    attemptsRemaining = Mathf.Max(1, candidateAttempts);
                    StartNextPathRequest();
                }
            }

            if (path.Count == 0)
                return pendingPath != null
                    ? NodeState.Running
                    : NodeState.Failure;

            float tolerance = Mathf.Max(0.001f, arrivalTolerance);
            float toleranceSquared = tolerance * tolerance;
            SkipReachedWaypoints(toleranceSquared);
            if (waypointIndex >= path.Count)
            {
                path.Clear();
                RequestEvaluation();
                return NodeState.Running;
            }

            Vector3 waypoint = CellCenter(
                path[waypointIndex],
                self.position.z);
            self.position = Vector3.MoveTowards(
                self.position,
                waypoint,
                Mathf.Max(0f, movementSpeed) * Time.deltaTime);
            SkipReachedWaypoints(toleranceSquared);
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

        private bool StartNextPathRequest()
        {
            CancelPendingPath();

            Vector2 away = self.position - lastThreatPosition;
            if (away.sqrMagnitude <= Mathf.Epsilon)
                away = UnityEngine.Random.insideUnitCircle.normalized;
            if (away.sqrMagnitude <= Mathf.Epsilon)
                away = Vector2.up;
            away.Normalize();

            float currentDistance =
                Vector2.Distance(self.position, lastThreatPosition);
            float travelDistance = Mathf.Max(
                1f,
                Mathf.Max(0f, safeDistance) - currentDistance + 1f);
            Vector2Int start = Vector2Int.FloorToInt(self.position);
            int totalAttempts = Mathf.Max(1, candidateAttempts);

            while (attemptsRemaining-- > 0)
            {
                int usedAttempt = totalAttempts - attemptsRemaining - 1;
                float angle = CandidateAngle(usedAttempt);
                Vector2 direction =
                    Quaternion.Euler(0f, 0f, angle) * away;
                Vector2Int destination = Vector2Int.FloorToInt(
                    (Vector2)self.position + direction * travelDistance);
                if (destination == start || !map.IsWalkable(destination))
                    continue;

                pathCancellation = new CancellationTokenSource();
                pendingPath = pathFinder.FindPathAsync(
                    start,
                    destination,
                    Mathf.Max(1, maximumVisitedTiles),
                    pathCancellation.Token);
                return true;
            }

            return false;
        }

        private bool ConsumePendingPath()
        {
            Task<List<Vector2Int>> completed = pendingPath;
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
            float interval = Mathf.Max(0f, repathInterval);
            nextRepathTime = Time.time + interval;
            if (interval > 0f)
                RequestEvaluation(interval);
            return true;
        }

        private bool TryGetThreatPosition(out Vector3 position)
        {
            position = default;
            if (!Blackboard.TryGetValue(
                    AiKeys.Resolve(targetKey),
                    out object value))
            {
                return false;
            }

            Transform target = AttackTarget.ResolveTransform(value);
            if (target == null)
                return false;

            position = target.position;
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

        private static float CandidateAngle(int attempt)
        {
            if (attempt == 0)
                return 0f;

            int step = (attempt + 1) / 2;
            return step * 30f * (attempt % 2 == 1 ? 1f : -1f);
        }

        private static Vector3 CellCenter(Vector2Int cell, float z)
        {
            return new Vector3(cell.x + 0.5f, cell.y + 0.5f, z);
        }
    }
}
