using System;
using System.Collections.Generic;
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

        protected override void OnEnter()
        {
            path.Clear();
            waypointIndex = 0;
            hasPath = TryCreatePath();
        }

        protected override NodeState OnTick()
        {
            if (!hasPath || self == null)
                return NodeState.Failure;

            float tolerance = Mathf.Max(0.001f, arrivalTolerance);
            float toleranceSquared = tolerance * tolerance;
            SkipReachedWaypoints(toleranceSquared);

            if (waypointIndex >= path.Count)
                return NodeState.Success;

            Vector3 target = CellCenter(path[waypointIndex], self.position.z);
            float movement = Mathf.Max(0f, movementSpeed) * Time.deltaTime;
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
            path.Clear();
            hasPath = false;
        }

        private bool TryCreatePath()
        {
            if (!Blackboard.TryGet(AiKeys.Self, out GameObject owner) ||
                owner == null ||
                !Blackboard.TryGet(
                    AiKeys.PathFindingMap,
                    out IPathFindingMap map) ||
                map == null ||
                !Blackboard.TryGet(
                    AiKeys.PathFindingService,
                    out IPathFindingService pathFinder) ||
                pathFinder == null)
                return false;

            self = owner.transform;
            Vector2Int start = Vector2Int.FloorToInt(self.position);
            int attempts = Mathf.Max(1, candidateAttempts);
            int searchBudget = Mathf.Max(1, maximumVisitedTiles);
            float searchRadius = Mathf.Max(0.5f, radius);

            for (int attempt = 0; attempt < attempts; attempt++)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle *
                                 searchRadius;
                Vector2Int destination = Vector2Int.FloorToInt(
                    (Vector2)self.position + offset);

                if (destination == start || !map.IsWalkable(destination))
                    continue;

                if (!pathFinder.TryFindPath(
                        start,
                        destination,
                        path,
                        searchBudget))
                    continue;

                waypointIndex = path.Count > 1 ? 1 : 0;
                Blackboard.Set(
                    AiKeys.Destination,
                    CellCenter(destination, self.position.z));
                return path.Count > 0;
            }

            path.Clear();
            return false;
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
