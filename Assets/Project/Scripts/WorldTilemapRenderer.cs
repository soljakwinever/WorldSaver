using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using IngameDebugConsole;
using Project.Scripts;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Gameplay;
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
    private const int PersistentLayerCount = 4;
    private const int RoofLayerIndex = PersistentLayerCount;
    private const int LayerCount = PersistentLayerCount + 1;
    private const int SnapshotBorder = 1;
    private static readonly int CellCount =
        ChunkBuildResult.ChunkSize * ChunkBuildResult.ChunkSize;
    private static readonly int SnapshotSize =
        ChunkBuildResult.ChunkSize + SnapshotBorder * 2;
    private static readonly int SnapshotCellCount =
        SnapshotSize * SnapshotSize;
    private static readonly Vector2Int[] CardinalDirections =
    {
        Vector2Int.left,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.up
    };
    private static readonly int WaterHeightId =
        Shader.PropertyToID("_WaterHeight");
    private static readonly int WaterEmissionDataId =
        Shader.PropertyToID("_WaterEmissionData");

    [SerializeField] private Material _groundMaterial;
    [SerializeField] private Material _waterMaterial;
    [SerializeField] private Material _coverageMaterial;
    [SerializeField, Min(1)] private int _colliderFullRebuildThreshold = 1000;
    [SerializeField, Min(1)] private int _maxConcurrentBakeTasks = 2;
    [SerializeField, Min(1)] private int _maxBakedChunksAppliedPerFrame = 2;
    [SerializeField, Min(1)] private int _biomeSearchRadius = 4096;
    [SerializeField, Min(1)] private int _biomeSearchSpacing = 16;
    [SerializeField, Min(1)] private int _biomeSearchSamplesPerFrame = 128;
    [Inject] private WorldGeneration _worldGeneration;
    [Inject] private PlayerDataController _player;
    [Inject] private Grid _grid;

    private readonly Dictionary<Vector2Int, ChunkRenderData> _chunks = new();
    private readonly Vector4[] _waterEmissionData =
        new Vector4[WaterTilePayload.MaxTextureIndex + 1];
    private readonly bool[] _waterEmissionAssigned =
        new bool[WaterTilePayload.MaxTextureIndex + 1];
    private readonly HashSet<int> _waterEmissionConflictWarnings = new();
    private readonly HashSet<Vector2Int> _dirtyBakeChunks = new();
    private readonly HashSet<Vector2Int> _runningBakeChunks = new();
    private readonly ConcurrentQueue<ChunkBakeOutput> _completedBakes = new();
    private readonly Stack<ChunkRenderData> _chunkDataPool = new();
    private readonly HashSet<Vector3Int> _liquidVisited = new();
    private readonly Queue<Vector3Int> _liquidFrontier = new();
    private readonly List<(ChunkRenderData Chunk, int Index)> _liquidPool = new();
    private readonly Dictionary<TileData, int> _liquidCounts = new();
    private readonly Dictionary<string, int> _snapshotGroupKeys =
        new(StringComparer.Ordinal);
    private readonly Dictionary<TileData, int> _snapshotTileKeys = new();
    private readonly TileChangeData[] _bakedChanges =
        new TileChangeData[CellCount];
    private int _nextBakeVersion;
    private Coroutine _biomeSearch;

    internal int RegisteredChunkCount => _chunks.Count;

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
        DebugLogConsole.AddCommand<string>(
            "biome.find",
            "Finds a biome and teleports the player to it. Quote names containing spaces.",
            DebugFindBiome,
            "biome");
        DebugLogConsole.AddCommand<string, int>(
            "biome.find",
            "Finds a biome within the given radius and teleports the player to it.",
            DebugFindBiomeWithinRadius,
            "biome",
            "radius");
    }

    private void OnDestroy()
    {
        DebugLogConsole.RemoveCommand<string>(DebugFindBiome);
        DebugLogConsole.RemoveCommand<string, int>(DebugFindBiomeWithinRadius);
    }

    private void Update()
    {
        ApplyCompletedBakes(_maxBakedChunksAppliedPerFrame);
        StartPendingBakes();
    }

    private void DebugFindBiome(string biomeName)
    {
        StartBiomeSearch(biomeName, _biomeSearchRadius);
    }

    private void DebugFindBiomeWithinRadius(string biomeName, int radius)
    {
        StartBiomeSearch(biomeName, radius);
    }

    private void StartBiomeSearch(string biomeName, int radius)
    {
        if (string.IsNullOrWhiteSpace(biomeName))
        {
            Debug.LogWarning("A biome name is required.");
            return;
        }

        if (radius < 0)
        {
            Debug.LogWarning("Biome search radius cannot be negative.");
            return;
        }

        if (_player == null || _grid == null)
        {
            Debug.LogWarning(
                "Biome search cannot teleport because the player or world grid is unavailable.");
            return;
        }

        if (_biomeSearch != null)
            StopCoroutine(_biomeSearch);

        Vector3Int playerCell = _grid.WorldToCell(_player.transform.position);
        Vector2Int origin = new(playerCell.x, playerCell.y);
        _biomeSearch = StartCoroutine(
            FindBiomeAndTeleport(biomeName.Trim(), origin, radius));
    }

    private IEnumerator FindBiomeAndTeleport(
        string biomeName,
        Vector2Int origin,
        int radius)
    {
        Debug.Log(
            $"Searching for biome '{biomeName}' within {radius} cells of {origin}...");

        int samplesThisFrame = 0;
        foreach (Vector2Int candidate in EnumerateBiomeSearchPositions(
                     origin,
                     radius,
                     _biomeSearchSpacing))
        {
            BiomeData biome = GetBiome(candidate);
            if (BiomeNameMatches(biome, biomeName))
            {
                TeleportPlayer(candidate);
                Debug.Log(
                    $"Found biome '{biome.biomeName}' at {candidate} and teleported the player.");
                _biomeSearch = null;
                yield break;
            }

            samplesThisFrame++;
            if (samplesThisFrame < _biomeSearchSamplesPerFrame)
                continue;

            samplesThisFrame = 0;
            yield return null;
        }

        Debug.LogWarning(
            $"Could not find biome '{biomeName}' within {radius} cells of {origin}.");
        _biomeSearch = null;
    }

    /// <summary>
    /// Searches progressively larger square rings around an origin. Spacing can
    /// be increased for fast searches because generated biome regions span many
    /// cells; every returned candidate is still verified against terrain data.
    /// </summary>
    public bool TryFindBiome(
        string biomeName,
        Vector2Int origin,
        int radius,
        int spacing,
        out Vector2Int position)
    {
        position = default;
        if (string.IsNullOrWhiteSpace(biomeName) || radius < 0)
            return false;

        string normalizedName = biomeName.Trim();
        foreach (Vector2Int candidate in EnumerateBiomeSearchPositions(
                     origin,
                     radius,
                     spacing))
        {
            if (!BiomeNameMatches(GetBiome(candidate), normalizedName))
                continue;

            position = candidate;
            return true;
        }

        return false;
    }

    private BiomeData GetBiome(Vector2Int worldCell)
    {
        // GetTile uses the same biome blend settings as chunk generation and
        // avoids the extra cliff samples performed by GetTerrainSample.
        _worldGeneration.GetTile(
            worldCell.x,
            worldCell.y,
            out BiomeBlend biome,
            out _,
            out _,
            out _);
        return biome.dominantBiome;
    }

    private void TeleportPlayer(Vector2Int worldCell)
    {
        Vector3 destination =
            _grid.CellToWorld(new Vector3Int(worldCell.x, worldCell.y, 0));
        Rigidbody2D body = _player.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.position = destination;
        }
        else
        {
            _player.transform.position = destination;
        }
    }

    private static bool BiomeNameMatches(BiomeData biome, string requestedName)
    {
        return biome != null &&
               (string.Equals(
                    biome.biomeName,
                    requestedName,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    biome.name,
                    requestedName,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<Vector2Int> EnumerateBiomeSearchPositions(
        Vector2Int origin,
        int radius,
        int spacing)
    {
        spacing = Mathf.Max(1, spacing);
        yield return origin;

        if (radius == 0)
            yield break;

        spacing = Mathf.Min(spacing, radius);
        int ring = spacing;
        while (ring <= radius)
        {
            for (int x = -ring; x <= ring; x += spacing)
            {
                yield return origin + new Vector2Int(x, ring);
                yield return origin + new Vector2Int(x, -ring);
            }

            for (int y = -ring + spacing; y < ring; y += spacing)
            {
                yield return origin + new Vector2Int(ring, y);
                yield return origin + new Vector2Int(-ring, y);
            }

            if (ring == radius)
                yield break;

            ring = Mathf.Min(ring + spacing, radius);
        }
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
        {
            renderer.sharedMaterial = material;
            if (layer == PersistentTileLayer.Water && _worldGeneration != null)
            {
                material.SetFloat(
                    WaterHeightId,
                    _worldGeneration.Elevation.waterHeight);
                material.SetVectorArray(
                    WaterEmissionDataId,
                    _waterEmissionData);
            }
        }

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

    public void ConfigureRoofTilemap(TilemapRenderer renderer)
    {
        if (renderer == null)
            return;

        renderer.chunkSize = new Vector3Int(
            ChunkBuildResult.ChunkSize,
            ChunkBuildResult.ChunkSize,
            ChunkBuildResult.ChunkSize);
        renderer.maxChunkCount = 1;
        renderer.sortingLayerID = SortingLayer.NameToID("Roof");
        if (_groundMaterial != null)
            renderer.sharedMaterial = _groundMaterial;
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

        ChunkRenderData chunk = replacedExisting
            ? previous
            : AcquireChunkRenderData();
        chunk.Reset(owner, chunkPosition, NextBakeVersion());
        CopyCells(chunkPosition, (int)PersistentTileLayer.Ground, groundTiles, chunk);
        RegisterWaterEmissionData(waterTiles);
        CopyCells(chunkPosition, (int)PersistentTileLayer.Water, waterTiles, chunk);
        CopyCells(chunkPosition, (int)PersistentTileLayer.Wall, wallTiles, chunk);
        CopyCells(chunkPosition, (int)PersistentTileLayer.Ceiling, ceilingTiles, chunk);
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

    public void SetRoofTiles(
        Vector2Int chunkPosition,
        IReadOnlyList<CellData> roofTiles)
    {
        if (!_chunks.TryGetValue(chunkPosition, out ChunkRenderData chunk))
            return;

        Array.Clear(
            chunk.Layers[RoofLayerIndex],
            0,
            chunk.Layers[RoofLayerIndex].Length);
        CopyCells(chunkPosition, RoofLayerIndex, roofTiles, chunk);
        ApplyRoofLayerImmediately(chunkPosition, chunk);
        MarkChunkAndLoadedNeighborsDirty(chunkPosition);
    }

    public void SetRoofColor(Vector3Int worldCell, Color color)
    {
        if (!TryGetChunkAndIndex(
                worldCell,
                out Vector2Int chunkPosition,
                out int index) ||
            !_chunks.TryGetValue(chunkPosition, out ChunkRenderData chunk))
        {
            return;
        }

        LogicalCell cell = chunk.Layers[RoofLayerIndex][index];
        if (cell.Tile == null)
            return;

        chunk.Layers[RoofLayerIndex][index] =
            new LogicalCell(cell.Tile, color);
        if (color.a <= 0.001f)
        {
            // Clear the currently baked visual immediately. The room system
            // will omit this cell from the next authoritative roof rebuild,
            // while retaining its own coverage reference for restoration.
            chunk.Owner.ClearBakedRoofTile(IndexToLocalCell(index));
            return;
        }
        chunk.Owner.SetBakedRoofColor(IndexToLocalCell(index), color);
    }

    public void RemoveChunk(Vector2Int chunkPosition)
    {
        if (!_chunks.Remove(chunkPosition, out ChunkRenderData removed))
            return;

        removed.Owner?.ClearBakedTiles();
        removed.Reset(null, default, NextBakeVersion());
        _chunkDataPool.Push(removed);
        // Removing a chunk can split a liquid body. Every resulting component
        // that could have changed touches one of the removed chunk's borders.
        ResolveLiquidPoolsTouchingChunk(chunkPosition, includeChunkCells: false);
        MarkLoadedNeighborsDirty(chunkPosition);
    }

    /// <summary>
    /// Authoritatively clears live render state before the generation preset
    /// changes. Already-running worker results retain their bake versions and
    /// will be rejected if they complete after destination chunks are added.
    /// </summary>
    public void ClearForPlaneTransition()
    {
        if (_chunks.Count > 0)
        {
            Vector2Int[] positions = new Vector2Int[_chunks.Count];
            _chunks.Keys.CopyTo(positions, 0);
            foreach (Vector2Int position in positions)
                RemoveChunk(position);
        }

        _dirtyBakeChunks.Clear();
        while (_completedBakes.TryDequeue(out ChunkBakeOutput output))
        {
            _runningBakeChunks.Remove(output.ChunkPosition);
            ReturnBakeOutput(output);
        }

        _liquidVisited.Clear();
        _liquidFrontier.Clear();
        _liquidPool.Clear();
        _liquidCounts.Clear();
        NextBakeVersion();
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
        _liquidVisited.Clear();

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
                ResolveLiquidPool(
                    start,
                    _liquidVisited,
                    _liquidFrontier,
                    _liquidPool,
                    _liquidCounts);
            }
        }

        // Include the four strips immediately outside the changed chunk. These
        // are required when replacing or removing a chunk, and are cheap on add.
        for (int offset = 0; offset < ChunkBuildResult.ChunkSize; offset++)
        {
            ResolveLiquidPool(new Vector3Int(startX - 1, startY + offset), _liquidVisited, _liquidFrontier, _liquidPool, _liquidCounts);
            ResolveLiquidPool(new Vector3Int(startX + ChunkBuildResult.ChunkSize, startY + offset), _liquidVisited, _liquidFrontier, _liquidPool, _liquidCounts);
            ResolveLiquidPool(new Vector3Int(startX + offset, startY - 1), _liquidVisited, _liquidFrontier, _liquidPool, _liquidCounts);
            ResolveLiquidPool(new Vector3Int(startX + offset, startY + ChunkBuildResult.ChunkSize), _liquidVisited, _liquidFrontier, _liquidPool, _liquidCounts);
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

        Color surfaceColor = WaterTilePayload.DecodeSurfaceColor(candidateColor);
        Color source = candidate.Color;
        Color target = winner.Color;
        Color remappedSurface = new(
            RemapColorChannel(surfaceColor.r, source.r, target.r),
            RemapColorChannel(surfaceColor.g, source.g, target.g),
            RemapColorChannel(surfaceColor.b, source.b, target.b),
            1f);
        return WaterTilePayload.Encode(
            remappedSurface,
            WaterTilePayload.DecodeDepth(candidateColor),
            winner.waterTextureIndex);
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

    /// <summary>
    /// Copies the visible ground, water, and wall colors for one chunk into a
    /// single map layer. This avoids UI code querying 3,072 cells through the
    /// public world-cell API or reading back rendered tilemap textures.
    /// </summary>
    public bool CopyMapColors(
        Vector2Int chunkPosition,
        Color32[] destination)
    {
        if (destination == null || destination.Length < CellCount)
            throw new ArgumentException(
                $"A map color buffer must contain at least {CellCount} cells.",
                nameof(destination));

        if (!_chunks.TryGetValue(chunkPosition, out ChunkRenderData chunk))
            return false;

        LogicalCell[] ground =
            chunk.Layers[(int)PersistentTileLayer.Ground];
        LogicalCell[] water =
            chunk.Layers[(int)PersistentTileLayer.Water];
        LogicalCell[] walls =
            chunk.Layers[(int)PersistentTileLayer.Wall];

        for (int index = 0; index < CellCount; index++)
        {
            LogicalCell cell = ground[index];
            bool isWater = false;
            if (water[index].Tile != null)
            {
                cell = water[index];
                isWater = true;
            }
            if (walls[index].Tile != null)
            {
                cell = walls[index];
                isWater = false;
            }
            if (chunk.TransientWalls[index].HasValue)
            {
                cell = chunk.TransientWalls[index].Value;
                isWater = false;
            }

            if (cell.Tile == null)
            {
                destination[index] = new Color32(0, 0, 0, 0);
                continue;
            }

            Color32 mapColor = isWater
                ? WaterTilePayload.DecodeSurfaceColor(cell.Color)
                : cell.Color;
            if (isWater)
                mapColor.a = byte.MaxValue;
            destination[index] = mapColor;
        }

        return true;
    }

    public void SetTile(
        PersistentTileLayer layer,
        Vector3Int worldCell,
        TileData tile,
        Color color)
    {
        if ((uint)layer >= PersistentLayerCount ||
            !TryGetChunkAndIndex(
                worldCell,
                out Vector2Int chunkPosition,
                out int index) ||
            !_chunks.TryGetValue(chunkPosition, out ChunkRenderData chunk))
        {
            return;
        }

        if (layer == PersistentTileLayer.Water)
            RegisterWaterEmissionData(tile);
        chunk.Layers[(int)layer][index] = new LogicalCell(tile, color);
        RebakeCellAndNeighbors(layer, worldCell);
    }

    private void RegisterWaterEmissionData(IReadOnlyList<CellData> cells)
    {
        for (int i = 0; i < (cells?.Count ?? 0); i++)
            RegisterWaterEmissionData(cells[i].Tile);
    }

    private void RegisterWaterEmissionData(TileData tile)
    {
        if (tile == null || _waterMaterial == null)
            return;

        int index = Mathf.Clamp(
            tile.waterTextureIndex,
            0,
            WaterTilePayload.MaxTextureIndex);
        Color emission = tile.waterEmissionColor;
        Vector4 value = new(
            emission.r,
            emission.g,
            emission.b,
            Mathf.Max(0f, tile.waterEmissionStrength));
        if (_waterEmissionAssigned[index])
        {
            if (_waterEmissionData[index] != value &&
                _waterEmissionConflictWarnings.Add(index))
            {
                Debug.LogWarning(
                    $"Water tiles sharing texture index {index} must also share emission settings. " +
                    $"Keeping the first registered settings; '{tile.name}' differs.",
                    tile);
            }
            return;
        }

        _waterEmissionAssigned[index] = true;
        _waterEmissionData[index] = value;
        _waterMaterial.SetVectorArray(
            WaterEmissionDataId,
            _waterEmissionData);
    }

    /// <summary>
    /// Sets an entity-owned wall without replacing the underlying world tile.
    /// World refreshes may update that underlying tile while this overlay
    /// remains authoritative until its owning entity removes it.
    /// </summary>
    public bool SetTransientWallTile(
        Vector3Int worldCell,
        TileData tile,
        Color color)
    {
        if (tile == null ||
            !TryGetChunkAndIndex(
                worldCell,
                out Vector2Int chunkPosition,
                out int index) ||
            !_chunks.TryGetValue(chunkPosition, out ChunkRenderData chunk))
        {
            return false;
        }

        chunk.TransientWalls[index] = new LogicalCell(tile, color);
        RebakeCellAndNeighbors(PersistentTileLayer.Wall, worldCell);
        return true;
    }

    /// <summary>
    /// Removes an entity-owned wall and reveals the current underlying world
    /// tile, if any.
    /// </summary>
    public bool ClearTransientWallTile(Vector3Int worldCell)
    {
        if (!TryGetChunkAndIndex(
                worldCell,
                out Vector2Int chunkPosition,
                out int index) ||
            !_chunks.TryGetValue(chunkPosition, out ChunkRenderData chunk) ||
            !chunk.TransientWalls[index].HasValue)
        {
            return false;
        }

        chunk.TransientWalls[index] = null;
        RebakeCellAndNeighbors(PersistentTileLayer.Wall, worldCell);
        return true;
    }

    public void SetColor(
        PersistentTileLayer layer,
        Vector3Int worldCell,
        Color color)
    {
        if ((uint)layer >= PersistentLayerCount ||
            !TryGetChunkAndIndex(
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

    public void SetTint(
        PersistentTileLayer layer,
        Vector3Int worldCell,
        Color color)
    {
        if ((uint)layer >= PersistentLayerCount ||
            !TryGetChunkAndIndex(
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
        chunk.Owner.SetBakedTint(layer, IndexToLocalCell(index), color);
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

        // A single invalidation is sufficient while a replacement is already
        // queued. Incrementing on every dirty notification caused loading
        // neighbors and liquid cells to repeatedly invalidate the same bake.
        bool newlyQueued = _dirtyBakeChunks.Add(chunkPosition);
        if (newlyQueued && _runningBakeChunks.Contains(chunkPosition))
            chunk.BakeVersion++;
    }

    private void RequeueDirty(Vector2Int chunkPosition)
    {
        if (_chunks.ContainsKey(chunkPosition))
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
            if (!_chunks.TryGetValue(candidate, out ChunkRenderData chunk) ||
                chunk.Owner == null ||
                !chunk.Owner.IsFullyInitialized)
            {
                continue;
            }

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
        int processed = 0;
        while (processed < limit &&
               _completedBakes.TryDequeue(out ChunkBakeOutput output))
        {
            processed++;
            _runningBakeChunks.Remove(output.ChunkPosition);
            if (output.Exception != null)
            {
                Debug.LogException(output.Exception);
                RequeueDirty(output.ChunkPosition);
                continue;
            }

            if (!_chunks.TryGetValue(
                    output.ChunkPosition,
                    out ChunkRenderData chunk))
            {
                ReturnBakeOutput(output);
                continue;
            }

            if (chunk.BakeVersion != output.Version)
            {
                ReturnBakeOutput(output);
                RequeueDirty(output.ChunkPosition);
                continue;
            }

            try
            {
                ApplyBakedChunk(output.ChunkPosition, output.NeighborMasks);
                chunk.HasAppliedBake = true;
            }
            finally
            {
                ReturnBakeOutput(output);
            }
        }
    }

    private void ApplyBakedChunk(
        Vector2Int chunkPosition,
        byte[] neighborMasks)
    {
        if (!_chunks.TryGetValue(chunkPosition, out ChunkRenderData chunk))
            return;

        int startX = chunkPosition.x * ChunkBuildResult.ChunkSize;
        int startY = chunkPosition.y * ChunkBuildResult.ChunkSize;
        for (int layerIndex = 0; layerIndex < LayerCount; layerIndex++)
        {
            int maskOffset = layerIndex * CellCount;

            for (int y = 0; y < ChunkBuildResult.ChunkSize; y++)
            {
                for (int x = 0; x < ChunkBuildResult.ChunkSize; x++)
                {
                    int index = x + y * ChunkBuildResult.ChunkSize;
                    Vector3Int worldCell = new(startX + x, startY + y, 0);
                    Vector3Int localCell = new(x, y, 0);
                    _bakedChanges[index] = BakeCell(
                        layerIndex,
                        worldCell,
                        localCell,
                        GetEffectiveCell(chunk, layerIndex, index),
                        neighborMasks[maskOffset + index]);
                }
            }

            if (layerIndex == RoofLayerIndex)
                chunk.Owner.ApplyBakedRoofTiles(_bakedChanges);
            else
                chunk.Owner.ApplyBakedTiles(
                    (PersistentTileLayer)layerIndex,
                    _bakedChanges);
        }
    }

    private void ApplyRoofLayerImmediately(
        Vector2Int chunkPosition,
        ChunkRenderData chunk)
    {
        int startX = chunkPosition.x * ChunkBuildResult.ChunkSize;
        int startY = chunkPosition.y * ChunkBuildResult.ChunkSize;
        for (int y = 0; y < ChunkBuildResult.ChunkSize; y++)
        {
            for (int x = 0; x < ChunkBuildResult.ChunkSize; x++)
            {
                int index = x + y * ChunkBuildResult.ChunkSize;
                _bakedChanges[index] = BakeCell(
                    RoofLayerIndex,
                    new Vector3Int(startX + x, startY + y, 0),
                    new Vector3Int(x, y, 0),
                    chunk.Layers[RoofLayerIndex][index]);
            }
        }

        // Roof visibility is gameplay state, so apply it synchronously. The
        // queued full bake still follows to resolve final cross-chunk masks.
        chunk.Owner.ApplyBakedRoofTiles(_bakedChanges);
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

                LogicalCell logical =
                    GetEffectiveCell(chunk, (int)layer, index);
                TileChangeData change = BakeCell(
                    (int)layer,
                    worldCell,
                    IndexToLocalCell(index),
                    logical);
                chunk.Owner.ApplyBakedTile(layer, change);

                // A full bake captured before this edit must not overwrite the
                // immediately updated cell or any affected border neighbor.
                if (_runningBakeChunks.Contains(chunkPosition) ||
                    !chunk.HasAppliedBake)
                {
                    MarkDirty(chunkPosition);
                }
            }
        }
    }

    private TileChangeData BakeCell(
        int layerIndex,
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
                        GetNeighborMask(layerIndex, worldCell, cell.Tile);
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
        int layerIndex,
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
            if (TryGetLogicalCell(
                    layerIndex,
                    neighborPosition,
                    out LogicalCell neighbor) &&
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
        int startX =
            chunkPosition.x * ChunkBuildResult.ChunkSize - SnapshotBorder;
        int startY =
            chunkPosition.y * ChunkBuildResult.ChunkSize - SnapshotBorder;
        int keyCount = LayerCount * SnapshotCellCount;
        int[] connectionKeys =
            System.Buffers.ArrayPool<int>.Shared.Rent(keyCount);
        Array.Clear(connectionKeys, 0, keyCount);

        try
        {
            for (int layerIndex = 0;
                 layerIndex < LayerCount;
                 layerIndex++)
            {
                int layerOffset = layerIndex * SnapshotCellCount;
                _snapshotGroupKeys.Clear();
                _snapshotTileKeys.Clear();
                int nextKey = 1;

                for (int y = 0; y < SnapshotSize; y++)
                {
                    for (int x = 0; x < SnapshotSize; x++)
                    {
                        Vector3Int worldCell =
                            new(startX + x, startY + y, 0);
                        if (!TryGetLogicalCell(
                                layerIndex,
                                worldCell,
                                out LogicalCell cell) ||
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
                            if (!_snapshotGroupKeys.TryGetValue(
                                    group,
                                    out key))
                            {
                                key = nextKey++;
                                _snapshotGroupKeys.Add(group, key);
                            }
                        }
                        else if (!_snapshotTileKeys.TryGetValue(
                                     cell.Tile,
                                     out key))
                        {
                            key = nextKey++;
                            _snapshotTileKeys.Add(cell.Tile, key);
                        }

                        connectionKeys[
                            layerOffset + x + y * SnapshotSize] = key;
                    }
                }
            }
        }
        catch
        {
            System.Buffers.ArrayPool<int>.Shared.Return(connectionKeys);
            throw;
        }

        return new ChunkBakeInput(
            chunkPosition,
            version,
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
            {
                ReturnBakeOutput(output);
                continue;
            }

            try
            {
                ApplyBakedChunk(position, output.NeighborMasks);
                chunk.HasAppliedBake = true;
            }
            finally
            {
                ReturnBakeOutput(output);
            }
        }
    }

    private static ChunkBakeOutput BakeMasks(ChunkBakeInput input)
    {
        int size = ChunkBuildResult.ChunkSize;
        int maskCount = LayerCount * CellCount;
        byte[] masks =
            System.Buffers.ArrayPool<byte>.Shared.Rent(maskCount);
        Array.Clear(masks, 0, maskCount);

        // Bit order matches AutoTileDirections: NW, N, NE, W, E, SW, S, SE.
        try
        {
            for (int layer = 0; layer < LayerCount; layer++)
            {
                int keyOffset = layer * SnapshotCellCount;
                int maskOffset = layer * CellCount;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int snapshotX = x + SnapshotBorder;
                        int snapshotY = y + SnapshotBorder;
                        int centerKey = input.ConnectionKeys[
                            keyOffset + snapshotX +
                            snapshotY * SnapshotSize];
                        if (centerKey == 0)
                            continue;

                        byte mask = 0;
                        for (int direction = 0;
                             direction < AutoTileDirections.Count;
                             direction++)
                        {
                            Vector2Int offset =
                                AutoTileDirections.Get(direction);
                            int neighborX = snapshotX + offset.x;
                            int neighborY = snapshotY + offset.y;
                            int neighborKey = input.ConnectionKeys[
                                keyOffset + neighborX +
                                neighborY * SnapshotSize];
                            if (neighborKey == centerKey)
                                mask |= (byte)(1 << direction);
                        }

                        masks[maskOffset + x + y * size] = mask;
                    }
                }
            }
        }
        catch
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(masks);
            throw;
        }
        finally
        {
            System.Buffers.ArrayPool<int>.Shared.Return(input.ConnectionKeys);
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
        if ((uint)layer >= PersistentLayerCount)
        {
            cell = default;
            return false;
        }

        return TryGetLogicalCell((int)layer, worldCell, out cell);
    }

    private bool TryGetLogicalCell(
        int layerIndex,
        Vector3Int worldCell,
        out LogicalCell cell)
    {
        cell = default;
        if ((uint)layerIndex >= LayerCount ||
            !TryGetChunkAndIndex(
                worldCell,
                out Vector2Int chunkPosition,
                out int index) ||
            !_chunks.TryGetValue(chunkPosition, out ChunkRenderData chunk))
        {
            return false;
        }

        cell = GetEffectiveCell(chunk, layerIndex, index);
        return true;
    }

    private static LogicalCell GetEffectiveCell(
        ChunkRenderData chunk,
        int layerIndex,
        int index)
    {
        if (layerIndex == (int)PersistentTileLayer.Wall &&
            chunk.TransientWalls[index].HasValue)
        {
            return chunk.TransientWalls[index].Value;
        }

        return chunk.Layers[layerIndex][index];
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

    private ChunkRenderData AcquireChunkRenderData()
    {
        return _chunkDataPool.Count > 0
            ? _chunkDataPool.Pop()
            : new ChunkRenderData();
    }

    private int NextBakeVersion()
    {
        unchecked
        {
            _nextBakeVersion++;
            if (_nextBakeVersion == 0)
                _nextBakeVersion++;
            return _nextBakeVersion;
        }
    }

    private static void ReturnBakeOutput(ChunkBakeOutput output)
    {
        if (output.NeighborMasks != null)
            System.Buffers.ArrayPool<byte>.Shared.Return(output.NeighborMasks);
    }

    private static void CopyCells(
        Vector2Int chunkPosition,
        int layerIndex,
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
            destination.Layers[layerIndex][index] =
                new LogicalCell(cell.Tile, cell.Color);
        }
    }

    private sealed class ChunkRenderData
    {
        public Chunk Owner;
        public Vector2Int Position;
        public int BakeVersion;
        public bool HasAppliedBake;
        public readonly LogicalCell[][] Layers = CreateLayers();
        public readonly LogicalCell?[] TransientWalls =
            new LogicalCell?[CellCount];
        public readonly TileData[] GeneratedLiquidCandidates =
            new TileData[CellCount];
        public readonly TileData[] ResolvedGeneratedLiquids =
            new TileData[CellCount];
        public readonly Color[] GeneratedLiquidColors =
            new Color[CellCount];

        public void Reset(
            Chunk owner,
            Vector2Int position,
            int bakeVersion)
        {
            Owner = owner;
            Position = position;
            BakeVersion = bakeVersion;
            HasAppliedBake = false;
            for (int i = 0; i < Layers.Length; i++)
                Array.Clear(Layers[i], 0, Layers[i].Length);
            Array.Clear(TransientWalls, 0, TransientWalls.Length);
            Array.Clear(
                GeneratedLiquidCandidates,
                0,
                GeneratedLiquidCandidates.Length);
            Array.Clear(
                ResolvedGeneratedLiquids,
                0,
                ResolvedGeneratedLiquids.Length);
            Array.Clear(
                GeneratedLiquidColors,
                0,
                GeneratedLiquidColors.Length);
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
        public readonly int[] ConnectionKeys;

        public ChunkBakeInput(
            Vector2Int chunkPosition,
            int version,
            int[] connectionKeys)
        {
            ChunkPosition = chunkPosition;
            Version = version;
            ConnectionKeys = connectionKeys;
        }
    }

    private readonly struct ChunkBakeOutput
    {
        public readonly Vector2Int ChunkPosition;
        public readonly int Version;
        public readonly byte[] NeighborMasks;
        public readonly Exception Exception;

        public ChunkBakeOutput(
            Vector2Int chunkPosition,
            int version,
            byte[] neighborMasks,
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
