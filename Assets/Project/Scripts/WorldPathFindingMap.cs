using System;
using System.Collections.Generic;
using System.Threading;
using Project.Scripts.Bus;
using Project.Scripts.DataTypes;
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
        IDisposable
    {
        private readonly WorldGeneration _worldGeneration;
        private readonly WorldData _worldData;
        private readonly MapSignalBus _mapSignals;
        private Dictionary<Vector2Int, LocalChunk> _localChunks = new();

        public WorldPathFindingMap(
            WorldGeneration worldGeneration,
            WorldData worldData,
            MapSignalBus mapSignals)
        {
            _worldGeneration = worldGeneration;
            _worldData = worldData;
            _mapSignals = mapSignals;
            _mapSignals.ChunkBuilt += OnChunkBuilt;
            _mapSignals.ChunkUnloaded += OnChunkUnloaded;
        }

        public void Dispose()
        {
            _mapSignals.ChunkBuilt -= OnChunkBuilt;
            _mapSignals.ChunkUnloaded -= OnChunkUnloaded;
        }

        public bool IsWalkable(Vector2Int worldCell)
        {
            if (TryGetLocalCell(
                    Volatile.Read(ref _localChunks),
                    worldCell,
                    out LocalChunk chunk,
                    out int index))
            {
                return chunk.Walkable[index];
            }

            TerrainSample sample =
                _worldGeneration.GetTerrainSample(worldCell.x, worldCell.y);
            return !sample.isWater && !sample.isCliff;
        }

        public float GetTraversalCost(Vector2Int worldCell)
        {
            if (TryGetLocalCell(
                    Volatile.Read(ref _localChunks),
                    worldCell,
                    out LocalChunk chunk,
                    out int index))
            {
                return chunk.TraversalCosts[index];
            }

            TerrainSample sample =
                _worldGeneration.GetTerrainSample(worldCell.x, worldCell.y);
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
            if (!ContainsCell(chunks, start) ||
                !ContainsCell(chunks, destination))
            {
                snapshot = null;
                return false;
            }

            snapshot = new LocalSnapshot(chunks);
            return true;
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
                    result.heights[i] > _worldData.waterHeight &&
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

        private static bool ContainsCell(
            Dictionary<Vector2Int, LocalChunk> chunks,
            Vector2Int cell)
        {
            return chunks.ContainsKey(ToChunkPosition(cell));
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

            public LocalChunk(bool[] walkable, float[] traversalCosts)
            {
                Walkable = walkable;
                TraversalCosts = traversalCosts;
            }
        }

        private sealed class LocalSnapshot : IPathFindingMap
        {
            private readonly Dictionary<Vector2Int, LocalChunk> _chunks;

            public LocalSnapshot(Dictionary<Vector2Int, LocalChunk> chunks)
            {
                _chunks = chunks;
            }

            public bool IsWalkable(Vector2Int worldCell)
            {
                return TryGetLocalCell(
                           _chunks,
                           worldCell,
                           out LocalChunk chunk,
                           out int index) &&
                       chunk.Walkable[index];
            }

            public float GetTraversalCost(Vector2Int worldCell)
            {
                return TryGetLocalCell(
                    _chunks,
                    worldCell,
                    out LocalChunk chunk,
                    out int index)
                    ? chunk.TraversalCosts[index]
                    : float.PositiveInfinity;
            }
        }
    }
}
