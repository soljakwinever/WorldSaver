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
    /// Pathfinds toward a walkable point beyond the main camera and remains
    /// running after leaving view. The running state lets an enclosing
    /// Tick Idle decorator keep the NPC eligible for offscreen recycling.
    /// </summary>
    [Serializable, AiNode(Name = "Wander Off Screen", Group = "Actions")]
    public sealed class WanderOffScreen : AiNode
    {
        [SerializeField, Min(0f), InputPort("Movement Speed")]
        private float movementSpeed = 2f;

        [SerializeField, Min(0.001f), InputPort("Arrival Tolerance")]
        private float arrivalTolerance = 0.08f;

        [SerializeField, Min(0f), InputPort("Viewport Margin")]
        private float viewportMargin = 0.1f;

        [SerializeField, Min(0f), InputPort("Distance Beyond Screen")]
        private float distanceBeyondScreen = 2f;

        [SerializeField, Min(0f), InputPort("Lateral Jitter")]
        private float lateralJitter = 4f;

        [SerializeField, Min(1), InputPort("Candidate Attempts")]
        private int candidateAttempts = 16;

        [SerializeField, Min(1), InputPort("Maximum Visited Tiles")]
        private int maximumVisitedTiles = 4096;

        [SerializeField, Tooltip("Keep retrying path selection instead of allowing lower-priority behavior branches.")]
        private bool retryUntilOutside;

        [SerializeField, Tooltip("Destroy this NPC after it leaves the viewport instead of leaving it eligible for recycling.")]
        private bool despawnWhenOutside;

        [NonSerialized] private readonly List<Vector2Int> path = new();
        [NonSerialized] private Transform self;
        [NonSerialized] private Camera camera;
        [NonSerialized] private IPathFindingMap map;
        [NonSerialized] private IPathFindingService pathFinder;
        [NonSerialized] private int waypointIndex;
        [NonSerialized] private Task<List<Vector2Int>> pendingPath;
        [NonSerialized] private CancellationTokenSource pathCancellation;
        [NonSerialized] private Vector2Int pendingDestination;
        [NonSerialized] private PathFindingQuery pathQuery;

        protected override void OnEnter()
        {
            path.Clear();
            waypointIndex = 0;
            CancelPendingPath();

            self = Blackboard.TryGet(
                       AiKeys.Self,
                       out GameObject owner) &&
                   owner != null
                ? owner.transform
                : null;
            Blackboard.TryGet(AiKeys.PathFindingMap, out map);
            Blackboard.TryGet(
                AiKeys.PathFindingService,
                out pathFinder);
            pathQuery = AiPathingUtility.CaptureQuery(owner);
            camera = Camera.main;

            if (self != null &&
                camera != null &&
                map != null &&
                pathFinder != null &&
                !IsOutsideViewport(
                    camera,
                    self.position,
                    viewportMargin))
            {
                StartPathRequest();
            }
        }

        protected override NodeState OnTick()
        {
            if (self == null || map == null || pathFinder == null)
                return retryUntilOutside
                    ? NodeState.Running
                    : NodeState.Failure;

            if (camera == null)
                camera = Camera.main;
            if (camera == null)
                return retryUntilOutside
                    ? NodeState.Running
                    : NodeState.Failure;

            if (IsOutsideViewport(
                    camera,
                    self.position,
                    viewportMargin))
            {
                CancelPendingPath();
                path.Clear();
                waypointIndex = 0;
                if (despawnWhenOutside)
                {
                    foreach (MonoBehaviour behaviour in
                             self.GetComponentsInChildren<MonoBehaviour>(true))
                        if (behaviour is IOffscreenDespawnHandler handler)
                            handler.PrepareForOffscreenDespawn();
                    UnityEngine.Object.Destroy(self.gameObject);
                    return NodeState.Success;
                }
                return NodeState.Running;
            }

            if (pendingPath != null)
            {
                if (!pendingPath.IsCompleted)
                    return NodeState.Running;
                if (!ConsumePendingPath())
                    return StartPathRequest() || retryUntilOutside
                        ? NodeState.Running
                        : NodeState.Failure;
            }

            float tolerance = Mathf.Max(0.001f, arrivalTolerance);
            SkipReachedWaypoints(tolerance * tolerance);
            if (waypointIndex >= path.Count)
            {
                path.Clear();
                waypointIndex = 0;
                return StartPathRequest()
                    || retryUntilOutside
                    ? NodeState.Running
                    : NodeState.Failure;
            }

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
                AiPathingUtility.GetActorSpeedMultiplier(self.gameObject) *
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
            ResetPath();
        }

        protected override void OnExit()
        {
            ResetPath();
        }

        private bool StartPathRequest()
        {
            CancelPendingPath();
            if (camera == null || self == null)
                return false;

            Vector3 viewport =
                camera.WorldToViewportPoint(self.position);
            if (viewport.z <= 0f)
                return false;

            Vector2 direction =
                new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
            if (direction.sqrMagnitude < 0.0001f)
                direction = UnityEngine.Random.insideUnitCircle;
            if (direction.sqrMagnitude < 0.0001f)
                direction = Vector2.right;
            direction.Normalize();

            Vector2 exitViewport = FindViewportExit(
                direction,
                Mathf.Max(0f, viewportMargin));
            Vector3 exitWorld = camera.ViewportToWorldPoint(
                new Vector3(
                    exitViewport.x,
                    exitViewport.y,
                    viewport.z));
            Vector3 outward =
                camera.transform.right * direction.x +
                camera.transform.up * direction.y;
            outward.z = 0f;
            if (outward.sqrMagnitude < 0.0001f)
                outward = (exitWorld - self.position).normalized;
            outward.Normalize();
            Vector3 lateral = new(-outward.y, outward.x, 0f);

            Vector2Int start =
                Vector2Int.FloorToInt(self.position);
            int attempts = Mathf.Max(1, candidateAttempts);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                Vector3 candidate = exitWorld +
                    outward * Mathf.Max(0f, distanceBeyondScreen) +
                    lateral * UnityEngine.Random.Range(
                        -Mathf.Max(0f, lateralJitter),
                        Mathf.Max(0f, lateralJitter));
                Vector2Int destination =
                    Vector2Int.FloorToInt(candidate);
                Vector3 destinationCenter =
                    CellCenter(destination, self.position.z);
                if (destination == start ||
                    !map.IsWalkable(destination) ||
                    !IsOutsideViewport(
                        camera,
                        destinationCenter,
                        viewportMargin))
                {
                    continue;
                }

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
            Blackboard.Set(
                AiKeys.Destination,
                CellCenter(destination, self.position.z));
            return true;
        }

        private void ResetPath()
        {
            CancelPendingPath();
            path.Clear();
            waypointIndex = 0;
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

        public static bool IsOutsideViewport(
            Camera camera,
            Vector3 worldPosition,
            float margin)
        {
            if (camera == null)
                return false;

            margin = Mathf.Max(0f, margin);
            Vector3 viewport =
                camera.WorldToViewportPoint(worldPosition);
            return viewport.z <= 0f ||
                   viewport.x < -margin ||
                   viewport.x > 1f + margin ||
                   viewport.y < -margin ||
                   viewport.y > 1f + margin;
        }

        private static Vector2 FindViewportExit(
            Vector2 direction,
            float margin)
        {
            float xBoundary = direction.x >= 0f
                ? 1f + margin
                : -margin;
            float yBoundary = direction.y >= 0f
                ? 1f + margin
                : -margin;
            float xScale = Mathf.Abs(direction.x) > 0.0001f
                ? (xBoundary - 0.5f) / direction.x
                : float.PositiveInfinity;
            float yScale = Mathf.Abs(direction.y) > 0.0001f
                ? (yBoundary - 0.5f) / direction.y
                : float.PositiveInfinity;
            float scale = Mathf.Min(xScale, yScale);
            return new Vector2(0.5f, 0.5f) + direction * scale;
        }

        private static Vector3 CellCenter(Vector2Int cell, float z)
        {
            return new Vector3(cell.x + 0.5f, cell.y + 0.5f, z);
        }
    }
}
