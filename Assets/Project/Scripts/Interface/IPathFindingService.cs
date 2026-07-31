using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Project.Scripts.Interface
{
    /// <summary>
    /// Immutable identity and immunity data captured before a path search moves
    /// to a worker thread.
    /// </summary>
    public readonly struct PathFindingQuery
    {
        public readonly string ActorId;
        public readonly string VillageId;
        public readonly string FactionId;
        public readonly string[] Immunities;

        public PathFindingQuery(
            string actorId,
            string villageId,
            string factionId,
            string[] immunities)
        {
            ActorId = actorId ?? string.Empty;
            VillageId = villageId ?? string.Empty;
            FactionId = factionId ?? string.Empty;
            Immunities = immunities ?? System.Array.Empty<string>();
        }

        public bool HasImmunity(string immunityId)
        {
            if (string.IsNullOrWhiteSpace(immunityId))
                return false;

            string[] values = Immunities ?? System.Array.Empty<string>();
            for (int i = 0; i < values.Length; i++)
            {
                if (string.Equals(
                        values[i],
                        immunityId,
                        System.StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }

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

    public interface IContextualPathFindingService
    {
        Task<List<Vector2Int>> FindPathAsync(
            Vector2Int start,
            Vector2Int destination,
            PathFindingQuery query,
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

    /// <summary>
    /// Builds agent-specific local snapshots for doors, hazards, and other
    /// navigation hints.
    /// </summary>
    public interface IContextualLocalPathFindingMap
    {
        bool TryCreateLocalSnapshot(
            Vector2Int start,
            Vector2Int destination,
            PathFindingQuery query,
            out IPathFindingMap snapshot);
    }

    /// <summary>
    /// Allows a movement action to prepare a hinted cell, such as opening an
    /// accessible door, immediately before entering it.
    /// </summary>
    public interface IPathTraversalHandler
    {
        bool TryPrepareTraversal(
            Vector2Int worldCell,
            PathFindingQuery query);

        float GetEffectiveTraversalCost(
            Vector2Int worldCell,
            PathFindingQuery query);

        bool TryBreachCell(Vector2Int worldCell, int damage);
        bool TryLockpickCell(Vector2Int worldCell, int skill);
        bool IsBreachableCell(Vector2Int worldCell);
    }
}
