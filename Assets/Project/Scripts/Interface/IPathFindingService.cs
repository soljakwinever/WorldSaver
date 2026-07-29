using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

        /// <summary>
        /// Finds a path on a worker thread. Returns null when no path exists.
        /// The returned list is not shared with the pathfinder's internal state.
        /// </summary>
        Task<List<Vector2Int>> FindPathAsync(
            Vector2Int start,
            Vector2Int destination,
            int maxVisitedTiles = 100000,
            CancellationToken cancellationToken = default);
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

    /// <summary>
    /// Provides an immutable, worker-safe view of currently loaded navigation
    /// chunks. Cells outside the captured view are treated as unwalkable.
    /// </summary>
    public interface ILocalPathFindingMap
    {
        bool TryCreateLocalSnapshot(
            Vector2Int start,
            Vector2Int destination,
            out IPathFindingMap snapshot);
    }
}
