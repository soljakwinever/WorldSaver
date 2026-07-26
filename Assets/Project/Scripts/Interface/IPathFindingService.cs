using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.Interface
{
    /// <summary>
    /// Finds tile paths in world-cell coordinates. Implementations must not
    /// require the corresponding Unity chunks to be loaded.
    /// </summary>
    public interface IPathFindingService
    {
        bool TryFindPath(
            Vector2Int start,
            Vector2Int destination,
            List<Vector2Int> path,
            int maxVisitedTiles = 100000);
    }

    /// <summary>
    /// Supplies navigation data independently of rendered/loaded chunks.
    /// Implement this using deterministic world generation plus persisted
    /// tile/entity overrides.
    /// </summary>
    public interface IPathFindingMap
    {
        bool IsWalkable(Vector2Int worldCell);
        float GetTraversalCost(Vector2Int worldCell);
    }
}
