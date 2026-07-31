using Project.Scripts.Interface;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Project.Scripts.AI
{
    internal static class AiPathingUtility
    {
        public static PathFindingQuery CaptureQuery(GameObject owner)
        {
            if (owner == null)
                return default;

            IAiPathingAgent agent =
                owner.GetComponentInParent<IAiPathingAgent>() ??
                owner.GetComponentInChildren<IAiPathingAgent>();
            return agent?.CapturePathFindingQuery() ?? default;
        }

        public static bool PrepareCell(
            IPathFindingMap map,
            Vector2Int cell,
            PathFindingQuery query)
        {
            return map is not IPathTraversalHandler handler ||
                   handler.TryPrepareTraversal(cell, query);
        }

        public static float GetSpeedMultiplier(
            IPathFindingMap map,
            Vector2Int cell,
            PathFindingQuery query)
        {
            float cost = map is IPathTraversalHandler handler
                ? handler.GetEffectiveTraversalCost(cell, query)
                : map?.GetTraversalCost(cell) ?? 1f;
            return float.IsNaN(cost) ||
                   float.IsInfinity(cost) ||
                   cost <= 0f
                ? 0f
                : 1f / Mathf.Max(1f, cost);
        }

        public static Task<List<Vector2Int>> FindPathAsync(
            IPathFindingService service,
            Vector2Int start,
            Vector2Int destination,
            PathFindingQuery query,
            int maximumVisitedTiles,
            CancellationToken cancellationToken)
        {
            return service is IContextualPathFindingService contextual
                ? contextual.FindPathAsync(
                    start,
                    destination,
                    query,
                    maximumVisitedTiles,
                    cancellationToken)
                : service.FindPathAsync(
                    start,
                    destination,
                    maximumVisitedTiles,
                    cancellationToken);
        }
    }
}
