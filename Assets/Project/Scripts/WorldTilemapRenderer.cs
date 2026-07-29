using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Project.Scripts;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;
using UnityEngine.Tilemaps;
using Zenject;
using TileData = Project.Scripts.DataTypes.TileData;

/// <summary>
/// Coordinates custom auto-tile baking across loaded chunk boundaries.
/// Logical data lives here for neighbor queries, while every baked visual is
/// written into the owning Chunk's local Tilemaps.
/// </summary>
public sealed class WorldTilemapRenderer : MonoBehaviour, IInitializable
{
    private const int LayerCount = 4;
    private static readonly int CellCount =
        ChunkBuildResult.ChunkSize * ChunkBuildResult.ChunkSize;
    private static readonly Vector2Int[] CardinalDirections =
    {
        Vector2Int.left,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.up
    };

    [SerializeField] private Material _groundMaterial;
    [SerializeField] private Material _waterMaterial;
    [SerializeField] private Material _coverageMaterial;
    [SerializeField, Min(1)] private int _colliderFullRebuildThreshold = 1000;
    [SerializeField, Min(1)] private int _maxConcurrentBakeTasks = 2;
    [SerializeField, Min(1)] private int _maxBakedChunksAppliedPerFrame = 2;
    [Inject] private WorldGeneration _worldGeneration;

    private readonly Dictionary<Vector2Int, ChunkRenderData> _chunks = new();
    private readonly HashSet<Vector2Int> _dirtyBakeChunks = new();
    private readonly HashSet<Vector2Int> _runningBakeChunks = new();
    private readonly ConcurrentQueue<ChunkBakeOutput> _completedBakes = new();

    public readonly struct CellData
    {
        public readonly Vector3Int Position;
        public readonly TileData Tile;
        public readonly Color Color;

        public CellData(Vector3Int position, TileData tile, Color color)
        {
            Position = position;
            Tile = tile;
            Color = color;
        }
    }

    public void Initialize()
    {
        // Kept as an initializable scene service. Chunks own all render objects.
    }

    private void Update()
    {
        ApplyCompletedBakes(_maxBakedChunksAppliedPerFrame);
        StartPendingBakes();
    }

    public void ConfigureChunkTilemap(
        PersistentTileLayer layer,
        TilemapRenderer renderer,
        TilemapCollider2D collider)
    {
        renderer.chunkSize = new Vector3Int(
            ChunkBuildResult.ChunkSize,
            ChunkBuildResult.ChunkSize,
            ChunkBuildResult.ChunkSize);
        // Each local Tilemap covers exactly one 32x32 renderer chunk.
        renderer.maxChunkCount = 1;

        Material material = layer == PersistentTileLayer.Water
            ? _waterMaterial
            : _groundMaterial;
        if (material != null)
            renderer.sharedMaterial = material;

        if (collider != null)
        {
            collider.maximumTileChangeCount =
                (uint)_colliderFullRebuildThreshold;
        }
    }

    public void ConfigureCoverageTilemap(TilemapRenderer renderer)
    {
        if (renderer == null)
            return;

        renderer.chunkSize = new Vector3Int(
            ChunkBuildResult.ChunkSize,
            ChunkBuildResult.ChunkSize,
            ChunkBuildResult.ChunkSize);
        renderer.maxChunkCount = 1;
        if (_groundMaterial != null)
            renderer.sharedMaterial = _coverageMaterial;
    }

    public void ApplyChunk(
        Chunk owner,
        Vector2Int chunkPosition,
        IReadOnlyList<CellData> groundTiles,
        IReadOnlyList<CellData> waterTiles,
        IReadOnlyList<CellData> wallTiles = null,
        IReadOnlyList<CellData> ceilingTiles = null)
    {
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));

        bool replacedExisting = _chunks.TryGetValue(
            chunkPosition,
            out ChunkRenderData previous);
        if (replacedExisting && previous.Owner != owner)
        {
            previous.Owner?.ClearBakedTiles();
        }

        ChunkRenderData chunk = new(owner, chunkPosition);
        CopyCells(chunkPosition, PersistentTileLayer.Ground, groundTiles, chunk);
        CopyCells(chunkPosition, PersistentTileLayer.Water, waterTiles, chunk);
        CopyCells(chunkPosition, PersistentTileLayer.Wall, wallTiles, chunk);
        CopyCells(chunkPosition, PersistentTileLayer.Ceiling, ceilingTiles, chunk);
        for (int i = 0; i < CellCount; i++)
        {
            TileData candidate = chunk.Layers[(int)PersistentTileLayer.Water][i].Tile;
            chunk.GeneratedLiquidCandidates[i] = candidate;
            chunk.ResolvedGeneratedLiquids[i] = candidate;
            chunk.GeneratedLiquidColors[i] =
                chunk.Layers[(int)PersistentTileLayer.Water][i].Color;
        }
        _chunks[chunkPosition] = chunk;

        // Registration happens before baking so border cells can read complete
        // arrays from any loaded neighbor, regardless of load order.
        ResolveLiquidPoolsTouchingChunk(chunkPosition, includeChunkCells: true);
        MarkChunkAndLoadedNeighborsDirty(chunkPosition);
    }

    public void RemoveChunk(Vector2Int chunkPosition)
    {
        if (!_chunks.Remove(chunkPosition, out ChunkRenderData removed))
            return;

        removed.Owner?.ClearBakedTiles();
        // Removing a chunk can split a liquid body. Every resulting component
        // that could have changed touches one of the removed chunk's borders.
        ResolveLiquidPoolsTouchingChunk(chunkPosition, includeChunkCells: false);
        MarkLoadedNeighborsDirty(chunkPosition);
    }

    /// <summary>
    /// Resolves each four-way-connected generated liquid body to its most common
    /// candidate type across the currently loaded chunks. Generated candidates
    /// remain unchanged so loading or unloading chunks can deterministically
    /// change the winner without accumulating previous results as new votes.
    /// </summary>
    private void ResolveLiquidPoolsTouchingChunk(
        Vector2Int chunkPosition,
        bool includeChunkCells)
    {
        HashSet<Vector3Int> visited = new();
        Queue<Vector3Int> frontier = new();
        List<(ChunkRenderData Chunk, int Index)> pool = new();
        Dictionary<TileData, int> counts = new();

        int startX = chunkPosition.x * ChunkBuildResult.ChunkSize;
        int startY = chunkPosition.y * ChunkBuildResult.ChunkSize;
        if (includeChunkCells &&
            _chunks.TryGetValue(chunkPosition, out ChunkRenderData changedChunk))
        {
            for (int index = 0; index < CellCount; index++)
            {
                if (changedChunk.GeneratedLiquidCandidates[index] == null)
                    continue;

                Vector3Int start = new(
                    startX + index % ChunkBuildResult.ChunkSize,
                    startY + index / ChunkBuildResult.ChunkSize,
                    0);
                ResolveLiquidPool(start, visited, frontier, pool, counts);
            }
        }

        // Include the four strips immediately outside the changed chunk. These
        // are required when replacing or removing a chunk, and are cheap on add.
        for (int offset = 0; offset < ChunkBuildResult.ChunkSize; offset++)
        {
            ResolveLiquidPool(new Vector3Int(startX - 1, startY + offset), visited, frontier, pool, counts);
            ResolveLiquidPool(new Vector3Int(startX + ChunkBuildResult.ChunkSize, startY + offset), visited, frontier, pool, counts);
            ResolveLiquidPool(new Vector3Int(startX + offset, startY - 1), visited, frontier, pool, counts);
            ResolveLiquidPool(new Vector3Int(startX + offset, startY + ChunkBuildResult.ChunkSize), visited, frontier, pool, counts);
        }
    }

    private void ResolveLiquidPool(
        Vector3Int start,
        HashSet<Vector3Int> visited,
        Queue<Vector3Int> frontier,
        List<(ChunkRenderData Chunk, int Index)> pool,
        Dictionary<TileData, int> counts)
    {
        if (visited.Contains(start) ||
            !TryGetGeneratedLiquid(start, out _, out _, out _))
            return;

        visited.Add(start);
        frontier.Clear();
        pool.Clear();
        counts.Clear();
        frontier.Enqueue(start);

        while (frontier.Count > 0)
        {
            Vector3Int position = frontier.Dequeue();
            if (!TryGetGeneratedLiquid(
                    position,
                    out ChunkRenderData liquidChunk,
                    out int liquidIndex,
                    out TileData candidate))
                continue;

            pool.Add((liquidChunk, liquidIndex));
            counts.TryGetValue(candidate, out int count);
            counts[candidate] = count + 1;

            for (int direction = 0; direction < CardinalDirections.Length; direction++)
            {
                Vector2Int offset = CardinalDirections[direction];
                Vector3Int neighbor = new(
                    position.x + offset.x,
                    position.y + offset.y,
                    0);
                if (visited.Add(neighbor) &&
                    TryGetGeneratedLiquid(neighbor, out _, out _, out _))
                    frontier.Enqueue(neighbor);
            }
        }

        TileData winner = SelectDominantLiquid(counts);
        for (int i = 0; i < pool.Count; i++)
        {
            (ChunkRenderData liquidChunk, int liquidIndex) = pool[i];
            TileData previousGenerated =
                liquidChunk.ResolvedGeneratedLiquids[liquidIndex];
            LogicalCell current =
                liquidChunk.Layers[(int)PersistentTileLayer.Water][liquidIndex];
            bool canReplace = current.Tile == previousGenerated;
            Color resolvedColor = RemapLiquidColor(
                liquidChunk.GeneratedLiquidColors[liquidIndex],
                liquidChunk.GeneratedLiquidCandidates[liquidIndex],
                winner);
            liquidChunk.ResolvedGeneratedLiquids[liquidIndex] = winner;
            liquidChunk.Owner.SetGeneratedLiquidBaseline(
                liquidIndex,
                winner,
                resolvedColor);
            if (!canReplace ||
                current.Tile == winner && current.Color == resolvedColor)
                continue;

            liquidChunk.Layers[(int)PersistentTileLayer.Water][liquidIndex] =
                new LogicalCell(winner, resolvedColor);
            MarkDirty(liquidChunk.Position);
        }
    }

    private bool TryGetGeneratedLiquid(
        Vector3Int worldCell,
        out ChunkRenderData chunk,
        out int index,
        out TileData candidate)
    {
        candidate = null;
        if (!TryGetChunkAndIndex(worldCell, out Vector2Int chunkPosition, out index) ||
            !_chunks.TryGetValue(chunkPosition, out chunk))
        {
            chunk = null;
            return false;
        }

        candidate = chunk.GeneratedLiquidCandidates[index];
        return candidate != null;
    }

    private static TileData SelectDominantLiquid(
        Dictionary<TileData, int> counts)
    {
        TileData winner = null;
        int winnerCount = -1;
        foreach (KeyValuePair<TileData, int> pair in counts)
        {
            if (pair.Value > winnerCount ||
                pair.Value == winnerCount &&
                CompareLiquidTieBreak(pair.Key, winner) < 0)
            {
                winner = pair.Key;
                winnerCount = pair.Value;
            }
        }

        return winner;
    }

    private static int CompareLiquidTieBreak(TileData left, TileData right)
    {
        if (right == null)
            return -1;
        int idComparison = left.TileId.CompareTo(right.TileId);
        return idComparison != 0
            ? idComparison
            : string.CompareOrdinal(left.name, right.name);
    }

    private static Color RemapLiquidColor(
        Color candidateColor,
        TileData candidate,
        TileData winner)
    {
        if (candidate == null || winner == null)
            return candidateColor;

        Color source = candidate.Color;
        Color target = winner.Color;
        return new Color(
            RemapColorChannel(candidateColor.r, source.r, target.r),
            RemapColorChannel(candidateColor.g, source.g, target.g),
            RemapColorChannel(candidateColor.b, source.b, target.b),
            RemapColorChannel(candidateColor.a, source.a, target.a));
    }

    private static float RemapColorChannel(
        float value,
        float sourceMultiplier,
        float targetMultiplier)
    {
        return Mathf.Approximately(sourceMultiplier, 0f)
            ? value * targetMultiplier
            : value / sourceMultiplier * targetMultiplier;
    }

    public bool TryGetTileData(
        PersistentTileLayer layer,
        Vector3Int worldCell,
        out TileData tile)
    {
        tile = null;
        return TryGetCell(layer, worldCell, out LogicalCell cell) &&
               (tile = cell.Tile) != null;
    }

    public bool HasTile(PersistentTileLayer layer, Vector3Int worldCell)
    {
        return TryGetTileData(layer, worldCell, out _);
    }

    public void SetTile(
        PersistentTileLayer layer,
        Vector3Int worldCell,
        TileData tile,
        Color color)
    {
        if (!TryGetChunkAndIndex(
                worldCell,
                out Vector2Int chunkPosition,
                out int index) ||
            !_chunks.TryGetValue(chunkPosition, out ChunkRenderData chunk))
        {
            return;
        }

        chunk.Layers[(int)layer][index] = new LogicalCell(tile, color);
        chunk.BakeVersion++;
        RebakeCellAndNeighbors(layer, worldCell);

        // A full bake already in flight contains the old logical snapshot.
        // Queue a replacement so an initial/stale result cannot overwrite this edit.
        if (_runningBakeChunks.Contains(chunkPosition) ||
            !chunk.HasAppliedBake)
        {
            MarkDirty(chunkPosition);
        }
    }

    public void SetColor(
        PersistentTileLayer layer,
        Vector3Int worldCell,
        Color color)
    {
        if (!TryGetChunkAndIndex(
                worldCell,
                out Vector2Int chunkPosition,
                out int index) ||
            !_chunks.TryGetValue(chunkPosition, out ChunkRenderData chunk))
        {
            return;
        }

        LogicalCell cell = chunk.Layers[(int)layer][index];
        if (cell.Tile == null)
            return;

        chunk.Layers[(int)layer][index] = new LogicalCell(cell.Tile, color);
        chunk.Owner.SetBakedColor(
            layer,
            IndexToLocalCell(index),
            color);
    }

    private void MarkChunkAndLoadedNeighborsDirty(Vector2Int chunkPosition)
    {
        MarkDirty(chunkPosition);
        MarkLoadedNeighborsDirty(chunkPosition);
    }

    private void MarkLoadedNeighborsDirty(Vector2Int center)
    {
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                if (x == 0 && y == 0)
                    continue;

                Vector2Int position = center + new Vector2Int(x, y);
                if (_chunks.ContainsKey(position))
                    MarkDirty(position);
            }
        }
    }

    private void MarkDirty(Vector2Int chunkPosition)
    {
        if (!_chunks.TryGetValue(chunkPosition, out ChunkRenderData chunk))
            return;

        chunk.BakeVersion++;
        _dirtyBakeChunks.Add(chunkPosition);
    }

    private void StartPendingBakes()
    {
        while (_runningBakeChunks.Count < _maxConcurrentBakeTasks &&
               TryTakeDirtyChunk(out Vector2Int chunkPosition))
        {
            if (!_chunks.TryGetValue(
                    chunkPosition,
                    out ChunkRenderData chunk))
            {
                continue;
            }

            ChunkBakeInput input = CaptureBakeInput(
                chunkPosition,
                chunk.BakeVersion);
            _runningBakeChunks.Add(chunkPosition);
            _ = Task.Run(() => BakeMasks(input))
                .ContinueWith(
                    task =>
                    {
                        if (task.Status == TaskStatus.RanToCompletion)
                            _completedBakes.Enqueue(task.Result);
                        else
                            _completedBakes.Enqueue(
                                ChunkBakeOutput.Failed(
                                    chunkPosition,
                                    input.Version,
                                    task.Exception));
                    },
                    TaskScheduler.Default);
        }
    }

    private bool TryTakeDirtyChunk(out Vector2Int chunkPosition)
    {
        Vector2Int selected = default;
        bool found = false;
        foreach (Vector2Int candidate in _dirtyBakeChunks)
        {
            if (_runningBakeChunks.Contains(candidate))
                continue;

            selected = candidate;
            found = true;
            break;
        }

        if (found)
        {
            _dirtyBakeChunks.Remove(selected);
            chunkPosition = selected;
            return true;
        }

        chunkPosition = default;
        return false;
    }

    private void ApplyCompletedBakes(int limit)
    {
        int applied = 0;
        while (applied < limit &&
               _completedBakes.TryDequeue(out ChunkBakeOutput output))
        {
            _runningBakeChunks.Remove(output.ChunkPosition);
            if (output.Exception != null)
            {
                Debug.LogException(output.Exception);
                if (_chunks.ContainsKey(output.ChunkPosition))
                    MarkDirty(output.ChunkPosition);
                continue;
            }

            if (!_chunks.TryGetValue(
                    output.ChunkPosition,
                    out ChunkRenderData chunk))
            {
                continue;
            }

            if (chunk.BakeVersion != output.Version)
            {
                MarkDirty(output.ChunkPosition);
                continue;
            }

            ApplyBakedChunk(output.ChunkPosition, output.NeighborMasks);
            chunk.HasAppliedBake = true;
            applied++;
        }
    }

    private void ApplyBakedChunk(
        Vector2Int chunkPosition,
        byte[][] neighborMasks)
    {
        if (!_chunks.TryGetValue(chunkPosition, out ChunkRenderData chunk))
            return;

        int startX = chunkPosition.x * ChunkBuildResult.ChunkSize;
        int startY = chunkPosition.y * ChunkBuildResult.ChunkSize;
        for (int layerIndex = 0; layerIndex < LayerCount; layerIndex++)
        {
            PersistentTileLayer layer = (PersistentTileLayer)layerIndex;
            TileChangeData[] changes = new TileChangeData[CellCount];

            for (int y = 0; y < ChunkBuildResult.ChunkSize; y++)
            {
                for (int x = 0; x < ChunkBuildResult.ChunkSize; x++)
                {
                    int index = x + y * ChunkBuildResult.ChunkSize;
                    Vector3Int worldCell = new(startX + x, startY + y, 0);
                    Vector3Int localCell = new(x, y, 0);
                    changes[index] = BakeCell(
                        layer,
                        worldCell,
                        localCell,
                        chunk.Layers[layerIndex][index],
                        neighborMasks[layerIndex][index]);
                }
            }

            chunk.Owner.ApplyBakedTiles(layer, changes);
        }
    }

    private void RebakeCellAndNeighbors(
        PersistentTileLayer layer,
        Vector3Int center)
    {
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                Vector3Int worldCell =
                    center + new Vector3Int(x, y, 0);
                if (!TryGetChunkAndIndex(
                        worldCell,
                        out Vector2Int chunkPosition,
                        out int index) ||
                    !_chunks.TryGetValue(
                        chunkPosition,
                        out ChunkRenderData chunk))
                {
                    continue;
                }

                LogicalCell logical = chunk.Layers[(int)layer][index];
                TileChangeData change = BakeCell(
                    layer,
                    worldCell,
                    IndexToLocalCell(index),
                    logical);
                chunk.Owner.ApplyBakedTile(layer, change);
            }
        }
    }

    private TileChangeData BakeCell(
        PersistentTileLayer layer,
        Vector3Int worldCell,
        Vector3Int localCell,
        LogicalCell cell,
        byte? prebakedNeighborMask = null)
    {
        TileBase tileBase = cell.Tile != null ? cell.Tile.TileBase : null;
        Matrix4x4 transform = Matrix4x4.identity;

        if (cell.Tile != null && cell.Tile.AutoTile != null)
        {
            byte mask = prebakedNeighborMask ??
                        GetNeighborMask(layer, worldCell, cell.Tile);
            float grassHeight = cell.Tile.IsGrass &&
                                cell.Tile.AutoTile.AllowGrassHeight
                ? _worldGeneration.GrassHeightNoise(
                    worldCell.x,
                    worldCell.y)
                : 0f;
            AutoTileResult result =
                cell.Tile.AutoTile.Resolve(mask, worldCell, grassHeight);
            tileBase = result.Tile;
            transform = result.Transform;
        }

        return new TileChangeData(
            localCell,
            tileBase,
            cell.Color,
            transform);
    }

    private byte GetNeighborMask(
        PersistentTileLayer layer,
        Vector3Int center,
        TileData centerTile)
    {
        byte mask = 0;
        AutoTileDefinition definition = centerTile.AutoTile;
        for (int i = 0; i < AutoTileDirections.Count; i++)
        {
            Vector2Int offset = AutoTileDirections.Get(i);
            Vector3Int neighborPosition =
                center + new Vector3Int(offset.x, offset.y, 0);
            if (TryGetCell(layer, neighborPosition, out LogicalCell neighbor) &&
                definition.Connects(centerTile, neighbor.Tile))
            {
                mask |= (byte)(1 << i);
            }
        }

        return mask;
    }

    private ChunkBakeInput CaptureBakeInput(
        Vector2Int chunkPosition,
        int version)
    {
        const int border = 1;
        int snapshotSize = ChunkBuildResult.ChunkSize + border * 2;
        int startX = chunkPosition.x * ChunkBuildResult.ChunkSize - border;
        int startY = chunkPosition.y * ChunkBuildResult.ChunkSize - border;
        int[][] connectionKeys = new int[LayerCount][];

        for (int layerIndex = 0; layerIndex < LayerCount; layerIndex++)
        {
            PersistentTileLayer layer = (PersistentTileLayer)layerIndex;
            int[] keys = new int[snapshotSize * snapshotSize];
            Dictionary<string, int> groupKeys =
                new(StringComparer.Ordinal);
            Dictionary<TileData, int> tileKeys = new();
            int nextKey = 1;

            for (int y = 0; y < snapshotSize; y++)
            {
                for (int x = 0; x < snapshotSize; x++)
                {
                    Vector3Int worldCell =
                        new(startX + x, startY + y, 0);
                    if (!TryGetCell(layer, worldCell, out LogicalCell cell) ||
                        cell.Tile == null)
                    {
                        continue;
                    }

                    AutoTileDefinition definition = cell.Tile.AutoTile;
                    string group = definition != null
                        ? definition.ConnectivityGroup
                        : null;
                    int key;
                    if (!string.IsNullOrWhiteSpace(group))
                    {
                        if (!groupKeys.TryGetValue(group, out key))
                        {
                            key = nextKey++;
                            groupKeys.Add(group, key);
                        }
                    }
                    else
                    {
                        if (!tileKeys.TryGetValue(cell.Tile, out key))
                        {
                            key = nextKey++;
                            tileKeys.Add(cell.Tile, key);
                        }
                    }

                    keys[x + y * snapshotSize] = key;
                }
            }

            connectionKeys[layerIndex] = keys;
        }

        return new ChunkBakeInput(
            chunkPosition,
            version,
            snapshotSize,
            connectionKeys);
    }

    /// <summary>
    /// Completes queued snapshots synchronously. Intended for deterministic
    /// editor tests and loading flows that must display a chunk immediately.
    /// Runtime streaming uses the worker queue from Update.
    /// </summary>
    public void CompletePendingBakesImmediately()
    {
        while (TryTakeDirtyChunk(out Vector2Int position))
        {
            if (!_chunks.TryGetValue(position, out ChunkRenderData chunk))
                continue;

            ChunkBakeOutput output = BakeMasks(
                CaptureBakeInput(position, chunk.BakeVersion));
            if (chunk.BakeVersion != output.Version)
                continue;

            ApplyBakedChunk(position, output.NeighborMasks);
            chunk.HasAppliedBake = true;
        }
    }

    private static ChunkBakeOutput BakeMasks(ChunkBakeInput input)
    {
        int size = ChunkBuildResult.ChunkSize;
        byte[][] masks = new byte[LayerCount][];
        for (int layer = 0; layer < LayerCount; layer++)
            masks[layer] = new byte[CellCount];

        // Bit order matches AutoTileDirections: NW, N, NE, W, E, SW, S, SE.
        int[] offsetX = { -1, 0, 1, -1, 1, -1, 0, 1 };
        int[] offsetY = { 1, 1, 1, 0, 0, -1, -1, -1 };
        for (int layer = 0; layer < LayerCount; layer++)
        {
            int[] keys = input.ConnectionKeys[layer];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int snapshotX = x + 1;
                    int snapshotY = y + 1;
                    int centerKey =
                        keys[snapshotX + snapshotY * input.SnapshotSize];
                    if (centerKey == 0)
                        continue;

                    byte mask = 0;
                    for (int direction = 0;
                         direction < AutoTileDirections.Count;
                         direction++)
                    {
                        int neighborX = snapshotX + offsetX[direction];
                        int neighborY = snapshotY + offsetY[direction];
                        int neighborKey =
                            keys[neighborX +
                                 neighborY * input.SnapshotSize];
                        if (neighborKey == centerKey)
                            mask |= (byte)(1 << direction);
                    }

                    masks[layer][x + y * size] = mask;
                }
            }
        }

        return new ChunkBakeOutput(
            input.ChunkPosition,
            input.Version,
            masks,
            null);
    }

    private bool TryGetCell(
        PersistentTileLayer layer,
        Vector3Int worldCell,
        out LogicalCell cell)
    {
        cell = default;
        if (!Enum.IsDefined(typeof(PersistentTileLayer), layer) ||
            !TryGetChunkAndIndex(
                worldCell,
                out Vector2Int chunkPosition,
                out int index) ||
            !_chunks.TryGetValue(chunkPosition, out ChunkRenderData chunk))
        {
            return false;
        }

        cell = chunk.Layers[(int)layer][index];
        return true;
    }

    private static bool TryGetChunkAndIndex(
        Vector3Int worldCell,
        out Vector2Int chunkPosition,
        out int index)
    {
        int size = ChunkBuildResult.ChunkSize;
        int chunkX = FloorDiv(worldCell.x, size);
        int chunkY = FloorDiv(worldCell.y, size);
        int localX = worldCell.x - chunkX * size;
        int localY = worldCell.y - chunkY * size;
        chunkPosition = new Vector2Int(chunkX, chunkY);
        index = localX + localY * size;
        return true;
    }

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }

    private static Vector3Int IndexToLocalCell(int index)
    {
        return new Vector3Int(
            index % ChunkBuildResult.ChunkSize,
            index / ChunkBuildResult.ChunkSize,
            0);
    }

    private static void CopyCells(
        Vector2Int chunkPosition,
        PersistentTileLayer layer,
        IReadOnlyList<CellData> source,
        ChunkRenderData destination)
    {
        if (source == null)
            return;

        int startX = chunkPosition.x * ChunkBuildResult.ChunkSize;
        int startY = chunkPosition.y * ChunkBuildResult.ChunkSize;
        for (int i = 0; i < source.Count; i++)
        {
            CellData cell = source[i];
            int localX = cell.Position.x - startX;
            int localY = cell.Position.y - startY;
            if (localX < 0 || localY < 0 ||
                localX >= ChunkBuildResult.ChunkSize ||
                localY >= ChunkBuildResult.ChunkSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(source),
                    $"Cell {cell.Position} is outside chunk {chunkPosition}.");
            }

            int index = localX + localY * ChunkBuildResult.ChunkSize;
            destination.Layers[(int)layer][index] =
                new LogicalCell(cell.Tile, cell.Color);
        }
    }

    private sealed class ChunkRenderData
    {
        public readonly Chunk Owner;
        public readonly Vector2Int Position;
        public int BakeVersion;
        public bool HasAppliedBake;
        public readonly LogicalCell[][] Layers = CreateLayers();
        public readonly TileData[] GeneratedLiquidCandidates =
            new TileData[CellCount];
        public readonly TileData[] ResolvedGeneratedLiquids =
            new TileData[CellCount];
        public readonly Color[] GeneratedLiquidColors =
            new Color[CellCount];

        public ChunkRenderData(Chunk owner, Vector2Int position)
        {
            Owner = owner;
            Position = position;
        }

        private static LogicalCell[][] CreateLayers()
        {
            LogicalCell[][] layers = new LogicalCell[LayerCount][];
            for (int i = 0; i < layers.Length; i++)
                layers[i] = new LogicalCell[CellCount];
            return layers;
        }
    }

    private readonly struct ChunkBakeInput
    {
        public readonly Vector2Int ChunkPosition;
        public readonly int Version;
        public readonly int SnapshotSize;
        public readonly int[][] ConnectionKeys;

        public ChunkBakeInput(
            Vector2Int chunkPosition,
            int version,
            int snapshotSize,
            int[][] connectionKeys)
        {
            ChunkPosition = chunkPosition;
            Version = version;
            SnapshotSize = snapshotSize;
            ConnectionKeys = connectionKeys;
        }
    }

    private readonly struct ChunkBakeOutput
    {
        public readonly Vector2Int ChunkPosition;
        public readonly int Version;
        public readonly byte[][] NeighborMasks;
        public readonly Exception Exception;

        public ChunkBakeOutput(
            Vector2Int chunkPosition,
            int version,
            byte[][] neighborMasks,
            Exception exception)
        {
            ChunkPosition = chunkPosition;
            Version = version;
            NeighborMasks = neighborMasks;
            Exception = exception;
        }

        public static ChunkBakeOutput Failed(
            Vector2Int chunkPosition,
            int version,
            AggregateException exception)
        {
            return new ChunkBakeOutput(
                chunkPosition,
                version,
                null,
                exception?.Flatten() ??
                new Exception("Unknown auto-tile bake failure."));
        }
    }

    private readonly struct LogicalCell
    {
        public readonly TileData Tile;
        public readonly Color Color;

        public LogicalCell(TileData tile, Color color)
        {
            Tile = tile;
            Color = color;
        }
    }
}
