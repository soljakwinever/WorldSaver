using System;
using System.Collections.Generic;
using System.Threading;
using Project.Scripts.Bus;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Pathfinding
{
    /// <summary>
    /// Dense navigation for loaded chunks with deterministic generated-world
    /// fallback for long-distance queries.
    /// </summary>
    public sealed class WorldPathFindingMap :
        IPathFindingMap,
        ILocalPathFindingMap,
        IContextualLocalPathFindingMap,
        IPathTraversalHandler,
        IAutomaticDoorTraversalHandler,
        IDisposable
    {
        private readonly WorldGeneration _worldGeneration;
        private readonly MapSignalBus _mapSignals;
        private readonly Chunkloader _chunkloader;
        private Dictionary<Vector2Int, LocalChunk> _localChunks = new();
        private readonly Dictionary<string, AutomaticDoorPassage>
            _automaticDoorPassages = new(StringComparer.Ordinal);

        public WorldPathFindingMap(
            WorldGeneration worldGeneration,
            MapSignalBus mapSignals,
            Chunkloader chunkloader)
        {
            _worldGeneration = worldGeneration;
            _mapSignals = mapSignals;
            _chunkloader = chunkloader;
            _mapSignals.ChunkBuilt += OnChunkBuilt;
            _mapSignals.ChunkUnloaded += OnChunkUnloaded;
            _mapSignals.NavigationCellChanged += OnNavigationCellChanged;
            _mapSignals.NavigationChunkChanged += OnNavigationChunkChanged;
        }

        public void Dispose()
        {
            foreach (KeyValuePair<string, AutomaticDoorPassage> passage in
                     _automaticDoorPassages)
                passage.Value.Door?.ReleaseAutomaticTraversal(passage.Key);
            _automaticDoorPassages.Clear();
            _mapSignals.ChunkBuilt -= OnChunkBuilt;
            _mapSignals.ChunkUnloaded -= OnChunkUnloaded;
            _mapSignals.NavigationCellChanged -= OnNavigationCellChanged;
            _mapSignals.NavigationChunkChanged -= OnNavigationChunkChanged;
        }

        public bool IsWalkable(Vector2Int worldCell)
        {
            if (TryGetLocalCell(
                    Volatile.Read(ref _localChunks),
                    worldCell,
                    out LocalChunk chunk,
                    out int index))
            {
                bool walkable = chunk.Walkable[index];
                float cost = chunk.TraversalCosts[index];
                ApplyLoadedCellHints(
                    worldCell,
                    default,
                    ref walkable,
                    ref cost);
                return walkable;
            }

            TerrainSample sample =
                _worldGeneration.GetTerrainSample(worldCell.x, worldCell.y);
            return sample.IsWalkable;
        }

        public float GetTraversalCost(Vector2Int worldCell)
        {
            if (TryGetLocalCell(
                    Volatile.Read(ref _localChunks),
                    worldCell,
                    out LocalChunk chunk,
                    out int index))
            {
                bool walkable = chunk.Walkable[index];
                float cost = chunk.TraversalCosts[index];
                ApplyLoadedCellHints(
                    worldCell,
                    default,
                    ref walkable,
                    ref cost);
                return walkable ? cost : float.PositiveInfinity;
            }

            TerrainSample sample =
                _worldGeneration.GetTerrainSample(worldCell.x, worldCell.y);
            if (!sample.IsWalkable)
                return float.PositiveInfinity;
            // Prefer roads and trails while retaining an admissible cost floor.
            return sample.isRoad ? 1f : sample.isTrail ? 1.1f : 1.25f;
        }

        public bool TryCreateLocalSnapshot(
            Vector2Int start,
            Vector2Int destination,
            out IPathFindingMap snapshot)
        {
            Dictionary<Vector2Int, LocalChunk> chunks =
                Volatile.Read(ref _localChunks);
            snapshot = CreateHintedSnapshot(
                chunks,
                start,
                destination,
                default);
            return true;
        }

        public bool TryCreateLocalSnapshot(
            Vector2Int start,
            Vector2Int destination,
            PathFindingQuery query,
            out IPathFindingMap snapshot)
        {
            Dictionary<Vector2Int, LocalChunk> chunks =
                Volatile.Read(ref _localChunks);
            snapshot = CreateHintedSnapshot(
                chunks,
                start,
                destination,
                query);
            return true;
        }

        public bool TryPrepareTraversal(
            Vector2Int worldCell,
            PathFindingQuery query)
        {
            return !DoorComponent.TryGetAt(worldCell, out DoorComponent door) ||
                   door.IsOpen ||
                   door.TryOpenFor(query);
        }

        public bool TryBeginAutomaticTraversal(string actorId,
            Vector2Int worldCell, PathFindingQuery query)
        {
            if (string.IsNullOrWhiteSpace(actorId)) return false;
            if (!DoorComponent.TryGetAt(worldCell, out DoorComponent door))
                return true;

            if (_automaticDoorPassages.TryGetValue(actorId,
                    out AutomaticDoorPassage existing))
            {
                if (existing.Door == door && door.isActiveAndEnabled)
                    return true;
                existing.Door?.ReleaseAutomaticTraversal(actorId);
                _automaticDoorPassages.Remove(actorId);
            }

            if (!door.TryAcquireAutomaticTraversal(actorId, query))
                return false;
            _automaticDoorPassages[actorId] =
                new AutomaticDoorPassage(door);
            return true;
        }

        public void UpdateAutomaticTraversal(string actorId,
            Vector2Int currentCell)
        {
            if (string.IsNullOrWhiteSpace(actorId) ||
                !_automaticDoorPassages.TryGetValue(actorId,
                    out AutomaticDoorPassage passage)) return;
            if (passage.Door == null || !passage.Door.isActiveAndEnabled ||
                !DoorComponent.TryGetAt(passage.Door.WorldCell,
                    out DoorComponent registered) || registered != passage.Door)
            {
                passage.Door?.ReleaseAutomaticTraversal(actorId);
                _automaticDoorPassages.Remove(actorId);
                return;
            }
            if (currentCell == passage.Door.WorldCell)
            {
                passage.Entered = true;
                return;
            }
            if (!passage.Entered) return;
            passage.Door.ReleaseAutomaticTraversal(actorId);
            _automaticDoorPassages.Remove(actorId);
        }

        public void CancelAutomaticTraversal(string actorId)
        {
            if (string.IsNullOrWhiteSpace(actorId) ||
                !_automaticDoorPassages.TryGetValue(actorId,
                    out AutomaticDoorPassage passage)) return;
            passage.Door?.ReleaseAutomaticTraversal(actorId);
            _automaticDoorPassages.Remove(actorId);
        }

        public float GetEffectiveTraversalCost(
            Vector2Int worldCell,
            PathFindingQuery query)
        {
            bool walkable = true;
            float cost = 1f;
            if (TryGetLocalCell(
                    Volatile.Read(ref _localChunks),
                    worldCell,
                    out LocalChunk chunk,
                    out int index))
            {
                walkable = chunk.Walkable[index];
                cost = chunk.TraversalCosts[index];
            }

            ApplyLoadedCellHints(
                worldCell,
                query,
                ref walkable,
                ref cost);
            return walkable ? Mathf.Max(1f, cost) : float.PositiveInfinity;
        }

        public bool TryBreachCell(Vector2Int worldCell, int damage)
        {
            if (damage <= 0)
                return false;
            if (DoorComponent.TryGetAt(worldCell, out DoorComponent door))
                return door.TryBreak(damage);
            if (_chunkloader == null ||
                !_chunkloader.TryGetLoadedChunk(
                    new Vector3Int(worldCell.x, worldCell.y, 0),
                    out Chunk chunk))
                return false;

            bool applied = chunk.TryDamageWall(
                new Vector3Int(worldCell.x, worldCell.y, 0),
                damage,
                WallDestructionType.Destroyed,
                out bool destroyed);
            return applied && destroyed;
        }

        public bool TryLockpickCell(Vector2Int worldCell, int skill)
        {
            return DoorComponent.TryGetAt(worldCell, out DoorComponent door) &&
                   door.TryLockpick(skill);
        }

        public bool IsBreachableCell(Vector2Int worldCell)
        {
            if (DoorComponent.TryGetAt(worldCell, out _))
                return true;
            return _chunkloader != null &&
                   _chunkloader.TryGetLoadedChunk(
                       new Vector3Int(worldCell.x, worldCell.y, 0),
                       out Chunk chunk) &&
                   chunk.TryGetTileData(
                       new Vector3Int(worldCell.x, worldCell.y, 0),
                       PersistentTileLayer.Wall,
                       out TileData wall) &&
                   wall != null &&
                   wall.IsWall;
        }

        private IPathFindingMap CreateHintedSnapshot(
            Dictionary<Vector2Int, LocalChunk> chunks,
            Vector2Int start,
            Vector2Int destination,
            PathFindingQuery query)
        {
            Vector2Int startChunk = ToChunkPosition(start);
            Vector2Int destinationChunk = ToChunkPosition(destination);
            int minimumX = Math.Min(startChunk.x, destinationChunk.x) - 1;
            int maximumX = Math.Max(startChunk.x, destinationChunk.x) + 1;
            int minimumY = Math.Min(startChunk.y, destinationChunk.y) - 1;
            int maximumY = Math.Max(startChunk.y, destinationChunk.y) + 1;
            Dictionary<Vector2Int, LocalChunk> captured =
                new();
            foreach (var pair in chunks)
            {
                if (pair.Key.x < minimumX ||
                    pair.Key.x > maximumX ||
                    pair.Key.y < minimumY ||
                    pair.Key.y > maximumY)
                {
                    continue;
                }

                LocalChunk source = pair.Value;
                EnsureHintCache(pair.Key, source);
                LocalChunk copy = new(
                    (bool[])source.HintWalkable.Clone(),
                    (float[])source.HintTraversalCosts.Clone());
                captured.Add(pair.Key, copy);

                for (int hintIndex = 0;
                     hintIndex < source.ContextualIndices.Count;
                     hintIndex++)
                {
                    int index = source.ContextualIndices[hintIndex];
                    bool walkable = copy.Walkable[index];
                    float cost = copy.TraversalCosts[index];
                    ApplyCachedQueryHints(
                        source,
                        index,
                        query,
                        ref walkable,
                        ref cost);
                    copy.Walkable[index] = walkable;
                    copy.TraversalCosts[index] = cost;
                }
            }

            return new LocalSnapshot(captured, _worldGeneration);
        }

        private void ApplyLoadedCellHints(
            Vector2Int worldCell,
            PathFindingQuery query,
            ref bool walkable,
            ref float traversalCost)
        {
            Dictionary<Vector2Int, LocalChunk> chunks =
                Volatile.Read(ref _localChunks);
            if (!TryGetLocalCell(
                    chunks,
                    worldCell,
                    out LocalChunk local,
                    out int index))
            {
                return;
            }

            Vector2Int chunkPosition = ToChunkPosition(worldCell);
            EnsureHintCache(chunkPosition, local);
            walkable = local.HintWalkable[index];
            traversalCost = local.HintTraversalCosts[index];
            ApplyCachedQueryHints(
                local,
                index,
                query,
                ref walkable,
                ref traversalCost);
        }

        private void EnsureHintCache(
            Vector2Int chunkPosition,
            LocalChunk local)
        {
            if (local.HintsValid)
                return;

            Array.Copy(
                local.Walkable,
                local.HintWalkable,
                local.Walkable.Length);
            Array.Copy(
                local.TraversalCosts,
                local.HintTraversalCosts,
                local.TraversalCosts.Length);
            Array.Clear(local.Doors, 0, local.Doors.Length);
            Array.Clear(
                local.GroundHazardImmunities,
                0,
                local.GroundHazardImmunities.Length);
            Array.Clear(
                local.CoverageHazardImmunities,
                0,
                local.CoverageHazardImmunities.Length);
            local.ContextualIndices.Clear();

            if (_chunkloader == null ||
                !_chunkloader.TryGetLoadedChunk(
                    new Vector3Int(
                        chunkPosition.x * ChunkBuildResult.ChunkSize,
                        chunkPosition.y * ChunkBuildResult.ChunkSize,
                        0),
                    out Chunk loaded))
            {
                local.HintsValid = true;
                return;
            }

            int originX = chunkPosition.x * ChunkBuildResult.ChunkSize;
            int originY = chunkPosition.y * ChunkBuildResult.ChunkSize;
            for (int index = 0;
                 index < local.HintWalkable.Length;
                 index++)
            {
                Vector3Int cell = new(
                    originX + index % ChunkBuildResult.ChunkSize,
                    originY + index / ChunkBuildResult.ChunkSize,
                    0);
                Vector2Int cell2D = new(cell.x, cell.y);

                if (loaded.TryGetTileData(
                        cell,
                        PersistentTileLayer.Wall,
                        out TileData wall) &&
                    wall != null &&
                    wall.IsWall)
                {
                    if (DoorComponent.TryGetAt(cell2D, out DoorComponent door))
                    {
                        local.Doors[index] = door.CapturePathingState();
                        local.HintWalkable[index] = true;
                    }
                    else
                    {
                        local.HintWalkable[index] = false;
                        local.HintTraversalCosts[index] =
                            float.PositiveInfinity;
                        continue;
                    }
                }

                if (loaded.TryGetTileData(
                        cell,
                        PersistentTileLayer.Ground,
                        out TileData ground) &&
                    ground != null)
                {
                    switch (ground.pathingTerrain)
                    {
                        case TileData.AiPathingTerrain.Difficult:
                            local.HintTraversalCosts[index] = Mathf.Max(
                                local.HintTraversalCosts[index],
                                Mathf.Max(1f, ground.pathingCost));
                            break;
                        case TileData.AiPathingTerrain.Hazard:
                            local.GroundHazardImmunities[index] =
                                ground.hazardImmunityId;
                            local.HintTraversalCosts[index] = Mathf.Max(
                                local.HintTraversalCosts[index],
                                Mathf.Max(1f, ground.pathingCost));
                            break;
                    }
                }

                if (loaded.TryGetPathingCoverage(
                        cell,
                        out CoverageData coverage,
                        out float amount))
                {
                    if (coverage.PathingTerrain ==
                        TileData.AiPathingTerrain.Hazard)
                    {
                        local.CoverageHazardImmunities[index] =
                            coverage.HazardImmunityId;
                    }
                    local.HintTraversalCosts[index] = Mathf.Max(
                        local.HintTraversalCosts[index],
                        Mathf.Lerp(
                            1f,
                            coverage.PathingCost,
                            Mathf.Clamp01(amount)));
                }

                if (local.Doors[index].HasDoor ||
                    !string.IsNullOrWhiteSpace(
                        local.GroundHazardImmunities[index]) ||
                    !string.IsNullOrWhiteSpace(
                        local.CoverageHazardImmunities[index]))
                {
                    local.ContextualIndices.Add(index);
                }
            }

            local.HintsValid = true;
        }

        private static void ApplyCachedQueryHints(
            LocalChunk local,
            int index,
            PathFindingQuery query,
            ref bool walkable,
            ref float traversalCost)
        {
            DoorPathingState door = local.Doors[index];
            if (door.HasDoor)
            {
                walkable = door.Allows(query);
                if (!walkable)
                    traversalCost = float.PositiveInfinity;
            }

            if (!walkable)
                return;

            string groundHazard = local.GroundHazardImmunities[index];
            string coverageHazard = local.CoverageHazardImmunities[index];
            if (!string.IsNullOrWhiteSpace(groundHazard) &&
                    !query.HasImmunity(groundHazard) ||
                !string.IsNullOrWhiteSpace(coverageHazard) &&
                    !query.HasImmunity(coverageHazard))
            {
                walkable = false;
                traversalCost = float.PositiveInfinity;
            }
        }

        private void OnChunkBuilt(ChunkBuildResult result)
        {
            int cellCount =
                ChunkBuildResult.ChunkSize * ChunkBuildResult.ChunkSize;
            bool[] walkable = new bool[cellCount];
            float[] traversalCosts = new float[cellCount];

            for (int i = 0; i < cellCount; i++)
            {
                walkable[i] =
                    result.terrainKinds[i] == TerrainKind.Floor &&
                    result.heights[i] >
                    _worldGeneration.Elevation.waterHeight &&
                    !result.isCliff[i];
                traversalCosts[i] = result.isRoad[i]
                    ? 1f
                    : result.isTrail[i]
                        ? 1.1f
                        : 1.25f;
            }

            Dictionary<Vector2Int, LocalChunk> updated =
                new(Volatile.Read(ref _localChunks))
                {
                    [result.chunkPosition] =
                        new LocalChunk(walkable, traversalCosts)
                };
            Volatile.Write(ref _localChunks, updated);
        }

        private void OnChunkUnloaded(Vector2Int position)
        {
            Dictionary<Vector2Int, LocalChunk> current =
                Volatile.Read(ref _localChunks);
            if (!current.ContainsKey(position))
                return;

            Dictionary<Vector2Int, LocalChunk> updated = new(current);
            updated.Remove(position);
            Volatile.Write(ref _localChunks, updated);
        }

        private void OnNavigationCellChanged(Vector2Int worldCell)
        {
            OnNavigationChunkChanged(ToChunkPosition(worldCell));
        }

        private void OnNavigationChunkChanged(Vector2Int chunkPosition)
        {
            Dictionary<Vector2Int, LocalChunk> chunks =
                Volatile.Read(ref _localChunks);
            if (chunks.TryGetValue(
                    chunkPosition,
                    out LocalChunk chunk))
            {
                chunk.HintsValid = false;
            }
        }

        private static bool TryGetLocalCell(
            Dictionary<Vector2Int, LocalChunk> chunks,
            Vector2Int cell,
            out LocalChunk chunk,
            out int index)
        {
            Vector2Int chunkPosition = ToChunkPosition(cell);
            if (!chunks.TryGetValue(chunkPosition, out chunk))
            {
                index = 0;
                return false;
            }

            int localX = cell.x -
                         chunkPosition.x * ChunkBuildResult.ChunkSize;
            int localY = cell.y -
                         chunkPosition.y * ChunkBuildResult.ChunkSize;
            index = localX + localY * ChunkBuildResult.ChunkSize;
            return true;
        }

        private static Vector2Int ToChunkPosition(Vector2Int cell)
        {
            return new Vector2Int(
                FloorDiv(cell.x, ChunkBuildResult.ChunkSize),
                FloorDiv(cell.y, ChunkBuildResult.ChunkSize));
        }

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }

        private sealed class LocalChunk
        {
            public readonly bool[] Walkable;
            public readonly float[] TraversalCosts;
            public readonly bool[] HintWalkable;
            public readonly float[] HintTraversalCosts;
            public readonly DoorPathingState[] Doors;
            public readonly string[] GroundHazardImmunities;
            public readonly string[] CoverageHazardImmunities;
            public readonly List<int> ContextualIndices = new();
            public bool HintsValid;

            public LocalChunk(bool[] walkable, float[] traversalCosts)
            {
                Walkable = walkable;
                TraversalCosts = traversalCosts;
                HintWalkable = new bool[walkable.Length];
                HintTraversalCosts = new float[traversalCosts.Length];
                Doors = new DoorPathingState[walkable.Length];
                GroundHazardImmunities = new string[walkable.Length];
                CoverageHazardImmunities = new string[walkable.Length];
            }
        }

        private sealed class AutomaticDoorPassage
        {
            public readonly DoorComponent Door;
            public bool Entered;

            public AutomaticDoorPassage(DoorComponent door) => Door = door;
        }

        private sealed class LocalSnapshot : IPathFindingMap
        {
            private readonly Dictionary<Vector2Int, LocalChunk> _chunks;
            private readonly WorldGeneration _worldGeneration;

            public LocalSnapshot(Dictionary<Vector2Int, LocalChunk> chunks,
                WorldGeneration worldGeneration)
            {
                _chunks = chunks;
                _worldGeneration = worldGeneration;
            }

            public bool IsWalkable(Vector2Int worldCell)
            {
                if (TryGetLocalCell(
                        _chunks,
                        worldCell,
                        out LocalChunk chunk,
                        out int index))
                    return chunk.Walkable[index];

                return _worldGeneration
                    .GetTerrainSample(worldCell.x, worldCell.y)
                    .IsWalkable;
            }

            public float GetTraversalCost(Vector2Int worldCell)
            {
                if (TryGetLocalCell(
                        _chunks,
                        worldCell,
                        out LocalChunk chunk,
                        out int index))
                    return chunk.TraversalCosts[index];

                TerrainSample sample = _worldGeneration.GetTerrainSample(
                    worldCell.x, worldCell.y);
                if (!sample.IsWalkable)
                    return float.PositiveInfinity;
                return sample.isRoad ? 1f : sample.isTrail ? 1.1f : 1.25f;
            }
        }
    }
}
