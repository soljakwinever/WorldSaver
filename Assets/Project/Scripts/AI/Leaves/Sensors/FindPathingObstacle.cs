using System;
using Project.Scripts.AI.GraphEditor;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Sensors
{
    [Serializable, AiNode(
        Name = "Find Pathing Obstacle",
        Group = "Sensors")]
    public sealed class FindPathingObstacle : AiNode
    {
        [SerializeField, Min(1), InputPort("Search Radius")]
        private int searchRadius = 2;

        protected override NodeState OnTick()
        {
            if (!Blackboard.TryGet(AiKeys.Self, out GameObject self) ||
                self == null ||
                !Blackboard.TryGet(
                    AiKeys.PathFindingMap,
                    out IPathFindingMap map) ||
                map is not IPathTraversalHandler handler)
                return NodeState.Failure;

            Vector2Int origin =
                Vector2Int.FloorToInt(self.transform.position);
            int radius = Mathf.Max(1, searchRadius);
            Vector2Int nearest = default;
            int nearestDistance = int.MaxValue;

            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    int distance = Mathf.Abs(x) + Mathf.Abs(y);
                    if (distance == 0 ||
                        distance > radius ||
                        distance >= nearestDistance)
                        continue;

                    Vector2Int candidate = origin + new Vector2Int(x, y);
                    if (!handler.IsBreachableCell(candidate))
                        continue;

                    nearest = candidate;
                    nearestDistance = distance;
                }
            }

            if (nearestDistance == int.MaxValue)
                return NodeState.Failure;

            Blackboard.Set(
                AiKeys.Destination,
                new Vector3(nearest.x + 0.5f, nearest.y + 0.5f, 0f));
            return NodeState.Success;
        }
    }

    /// <summary>
    /// Finds the first breachable cell on the grid line from this AI toward
    /// its target. The target remains untouched; the obstacle position is
    /// written to Destination for a subsequent breach action.
    /// </summary>
    [Serializable, AiNode(
        Name = "Find Blocking Obstacle Toward Target",
        Group = "Sensors")]
    public sealed class FindBlockingObstacleTowardTarget : AiNode
    {
        [SerializeField, InputPort("Target")]
        private AiKeys.Key targetKey = AiKeys.Key.Target;

        [SerializeField, Min(1), InputPort("Maximum Search Distance")]
        private int maximumSearchDistance = 2;

        protected override NodeState OnTick()
        {
            if (!Blackboard.TryGet(AiKeys.Self, out GameObject self) ||
                self == null ||
                !Blackboard.TryGet(
                    AiKeys.PathFindingMap,
                    out IPathFindingMap map) ||
                map is not IPathTraversalHandler handler ||
                !SpatialSensorUtility.TryGetPosition(
                    Blackboard,
                    targetKey,
                    out Vector3 targetPosition))
            {
                return NodeState.Failure;
            }

            Vector2Int origin =
                Vector2Int.FloorToInt(self.transform.position);
            Vector2Int target = Vector2Int.FloorToInt(targetPosition);
            if (!TryFindFirstBreachableCell(
                    origin,
                    target,
                    Mathf.Max(1, maximumSearchDistance),
                    handler,
                    out Vector2Int obstacle))
            {
                return NodeState.Failure;
            }

            Blackboard.Set(
                AiKeys.Destination,
                new Vector3(
                    obstacle.x + 0.5f,
                    obstacle.y + 0.5f,
                    self.transform.position.z));
            return NodeState.Success;
        }

        internal static bool TryFindFirstBreachableCell(
            Vector2Int origin,
            Vector2Int target,
            int maximumDistance,
            IPathTraversalHandler handler,
            out Vector2Int obstacle)
        {
            obstacle = default;
            if (handler == null ||
                origin == target ||
                maximumDistance <= 0)
            {
                return false;
            }

            int x = origin.x;
            int y = origin.y;
            int deltaX = Mathf.Abs(target.x - origin.x);
            int deltaY = Mathf.Abs(target.y - origin.y);
            int stepX = origin.x < target.x ? 1 : -1;
            int stepY = origin.y < target.y ? 1 : -1;
            int error = deltaX - deltaY;

            for (int distance = 1;
                 distance <= maximumDistance &&
                 (x != target.x || y != target.y);
                 distance++)
            {
                int doubledError = error * 2;
                if (doubledError > -deltaY)
                {
                    error -= deltaY;
                    x += stepX;
                }
                if (doubledError < deltaX)
                {
                    error += deltaX;
                    y += stepY;
                }

                Vector2Int candidate = new(x, y);
                if (!handler.IsBreachableCell(candidate))
                    continue;

                obstacle = candidate;
                return true;
            }

            return false;
        }
    }
}
