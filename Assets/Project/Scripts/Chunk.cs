using System;
using System.Collections.Generic;
using System.Linq;
using Project.Scripts;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using Project.Scripts.TimeAndWeather;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Tilemaps;
using Zenject;
using TileData = Project.Scripts.DataTypes.TileData;

[RequireComponent(typeof(ChunkPersistenceRoot))]
[RequireComponent(typeof(TileCoverageComponent))]
public class Chunk : MonoBehaviour, IChunk
{
    [Header("Tilemaps")]
    [SerializeField] private Tilemap _groundTilemap;
    [SerializeField] private Tilemap _wallTilemap;
    [SerializeField] private Tilemap _ceilingTilemap;
    [SerializeField] private Tilemap _waterTilemap;
    [SerializeField] private Tilemap _coverageTilemap;

    [Header("Transforms")]
    [SerializeField] private Transform _nodeTransform;
    
    public Vector2Int Position { get; set; }

    [Inject] private WorldData worldData;
    [Inject] private WorldGeneration worldGeneration;
    [Inject] private ITimeController timeController;
    [Inject] private IRegionalWeatherService weatherService;

    [Header("Debug")]
    public TileData DebugTile;

    [Header("Default Tiles")]
    public TileData GroundTile;
    public TileData PathTile;
    public TileData WaterTile;
    public TileData CliffTile;
    public TileData BeachTile;
    public TileData WallTile;

    [Inject] private Node.Pool nodePool;
    [Inject] private DataController dataController;
    [Inject] private WorldTilemapRenderer worldTilemapRenderer;
    [Inject] private Chunkloader chunkloader;

    readonly List<Node> props = new();
    private ChunkPersistenceRoot _persistenceRoot;
    private TileCoverageComponent _coverage;
    private readonly Dictionary<CoverageData, Tile> _coverageTiles = new();
    private MaterialPropertyBlock _coveragePropertyBlock;
    private static readonly int CoverageTilingId =
        Shader.PropertyToID("_CoverageTiling");
    private static readonly int WorldOffsetId =
        Shader.PropertyToID("_WorldOffset");

    private readonly TileData[][] _baselineTiles = CreateLayerBuffers<TileData>();
    private readonly Color[][] _baselineColors = CreateLayerBuffers<Color>();
    private readonly PersistentTileTint[][] _baselineTints =
        CreateLayerBuffers<PersistentTileTint>();
    private readonly BiomeBlend[] _biomeBlends =
        new BiomeBlend[ChunkBuildResult.ChunkSize * ChunkBuildResult.ChunkSize];
    private readonly int[] _biomeColorRefreshOrder =
        new int[ChunkBuildResult.ChunkSize * ChunkBuildResult.ChunkSize];
    private readonly int[] _biomeColorAppliedDayKeys =
        new int[ChunkBuildResult.ChunkSize * ChunkBuildResult.ChunkSize];
    private bool _tilemapsConfigured;
    private int _biomeColorRefreshIndex = -1;
    private int _biomeColorRefreshCount;

    public int RemainingBiomeColorRefreshCells =>
        _biomeColorRefreshIndex < 0
            ? 0
            : _biomeColorRefreshCount - _biomeColorRefreshIndex;

    private static T[][] CreateLayerBuffers<T>()
    {
        int layerCount = Enum.GetValues(typeof(PersistentTileLayer)).Length;
        int cellCount = ChunkBuildResult.ChunkSize * ChunkBuildResult.ChunkSize;
        T[][] buffers = new T[layerCount][];
        for (int i = 0; i < layerCount; i++)
            buffers[i] = new T[cellCount];
        return buffers;
    }

    public void Init(ChunkBuildResult data)
    {
        EnsureTilemaps();
        ClearBakedTiles();
        Position = data.chunkPosition;
        _persistenceRoot.BeginRestore(Position);
        _coverage.Configure(
            this,
            data,
            worldGeneration.Elevation.waterHeight,
            worldData.coverageLayers);
        
        this.name = $"Chunk_{Position.x},{Position.y}";

        transform.position = new Vector3(Position.x * ChunkBuildResult.ChunkSize,
            Position.y * ChunkBuildResult.ChunkSize, 0);

        int offsetX = Position.x * ChunkBuildResult.ChunkSize;
        int offsetY = Position.y * ChunkBuildResult.ChunkSize;

        int cellCount = ChunkBuildResult.ChunkSize * ChunkBuildResult.ChunkSize;
        List<WorldTilemapRenderer.CellData> changes = new(cellCount);
        List<WorldTilemapRenderer.CellData> waterTiles = new(cellCount);
        List<WorldTilemapRenderer.CellData> wallTiles = new(cellCount);
        List<WorldTilemapRenderer.CellData> ceilingTiles = new(cellCount);

        for (int y = 0; y < ChunkBuildResult.ChunkSize; y++)
        {
            for (int x = 0; x < ChunkBuildResult.ChunkSize; x++)
            {
                int tileIndex = x + y * ChunkBuildResult.ChunkSize;
                var height = data.heights[tileIndex];
                var biome = data.biomeData[tileIndex];
                var moisture = data.moisture[tileIndex];
                var temperature = data.temperature[tileIndex];
                var isCliff = data.isCliff[tileIndex];
                var isWater = height <= worldGeneration.Elevation.waterHeight;

                Vector3Int tilePosition =
                    new Vector3Int(offsetX + x, offsetY + y, 0);
                SeasonalBiomeTint.Reapply(
                    ref biome,
                    worldData,
                    timeController,
                    offsetX + x,
                    offsetY + y);

                var tileData = GetTile(offsetX + x, offsetY + y, biome, height, moisture, temperature, isCliff,
                    out var color);
                color *= tileData.Color;
                bool isWall = isCliff || tileData.IsWall;
                if (isWall)
                {
                    wallTiles.Add(new WorldTilemapRenderer.CellData(
                        tilePosition, tileData, color));
                    if (tileData.ceilingTile != null)
                    {
                        ceilingTiles.Add(new WorldTilemapRenderer.CellData(
                            tilePosition,
                            tileData.ceilingTile,
                            tileData.ceilingTile.Color));
                    }
                }
                else
                {
                    changes.Add(new WorldTilemapRenderer.CellData(
                        tilePosition, tileData, color));
                }
                _biomeBlends[tileIndex] = biome;
                int scheduledHour = SeasonalBiomeTint.GetScheduledHour(
                    offsetX + x,
                    offsetY + y,
                    timeController.DayInMonth,
                    timeController.Season,
                    timeController.Year);
                _biomeColorAppliedDayKeys[tileIndex] =
                    scheduledHour <= timeController.Hour
                        ? SeasonalBiomeTint.GetDayKey(
                            timeController.DayInMonth,
                            timeController.Season,
                            timeController.Year)
                        : 0;
                _baselineTiles[(int)PersistentTileLayer.Ground][tileIndex] =
                    isWall ? null : tileData;
                _baselineColors[(int)PersistentTileLayer.Ground][tileIndex] = color;
                _baselineTints[(int)PersistentTileLayer.Ground][tileIndex] =
                    GetGroundTint(height, isCliff);
                _baselineTiles[(int)PersistentTileLayer.Water][tileIndex] = null;
                _baselineColors[(int)PersistentTileLayer.Water][tileIndex] = Color.white;
                _baselineTints[(int)PersistentTileLayer.Water][tileIndex] =
                    PersistentTileTint.BiomeWater;
                _baselineTiles[(int)PersistentTileLayer.Wall][tileIndex] =
                    isWall ? tileData : null;
                _baselineColors[(int)PersistentTileLayer.Wall][tileIndex] = color;
                _baselineTints[(int)PersistentTileLayer.Wall][tileIndex] =
                    GetGroundTint(height, isCliff);
                TileData ceiling = isWall ? tileData.ceilingTile : null;
                _baselineTiles[(int)PersistentTileLayer.Ceiling][tileIndex] = ceiling;
                _baselineColors[(int)PersistentTileLayer.Ceiling][tileIndex] =
                    ceiling != null ? ceiling.Color : Color.white;
                _baselineTints[(int)PersistentTileLayer.Ceiling][tileIndex] =
                    PersistentTileTint.TileDefault;

                if (isWater && !worldGeneration.Preset.heightMapDebug)
                {
                    tileData = biome.dominantBiome.overrideWaterTile ?? WaterTile;
                    Color waterColor = new(
                        Mathf.InverseLerp(
                            0,
                            worldGeneration.Elevation.waterHeight,
                            height),
                        Mathf.InverseLerp(
                            0,
                            worldGeneration.Elevation.waterHeight,
                            height),
                        Mathf.InverseLerp(
                            0,
                            worldGeneration.Elevation.waterHeight,
                            height));
                    waterColor *= tileData.Color;
                    waterTiles.Add(new WorldTilemapRenderer.CellData(
                        tilePosition, tileData, waterColor));
                    _baselineTiles[(int)PersistentTileLayer.Water][tileIndex] = tileData;
                    _baselineColors[(int)PersistentTileLayer.Water][tileIndex] = waterColor;
                }
            }
        }

        worldTilemapRenderer.ApplyChunk(
            this,
            Position,
            changes,
            waterTiles,
            wallTiles,
            ceilingTiles);

        if (worldGeneration.Preset.heightMapDebug) return;

        foreach (var propSpawnData in data.props)
        {
            var rule = worldGeneration.PropSpawnRules.First(
                t => t.name == propSpawnData.propName);

            var prop = nodePool.Spawn(propSpawnData.NodeId, propSpawnData, rule.nodeData, propSpawnData.terrainSample,
                this);
            prop.transform.SetParent(_nodeTransform);

            props.Add(prop);
            _persistenceRoot.RegisterGeneratedEntity(
                prop.GetComponent<PersistentEntity>());
        }

        RestorePersistentState();
    }

    internal void SetGeneratedLiquidBaseline(
        int index,
        TileData liquid,
        Color color)
    {
        if ((uint)index >= _baselineTiles[(int)PersistentTileLayer.Water].Length)
            return;

        _baselineTiles[(int)PersistentTileLayer.Water][index] = liquid;
        _baselineColors[(int)PersistentTileLayer.Water][index] = color;
    }

    private void UnloadProps()
    {
        foreach (var prop in props)
        {
            nodePool.Despawn(prop);
        }

        props.Clear();
    }

    [Inject] private WorldData _worldData;

    private void Awake()
    {
        _persistenceRoot = GetComponent<ChunkPersistenceRoot>();
        _coverage = GetComponent<TileCoverageComponent>();
        _persistenceRoot.SetRuntimeEntityFactory(RestoreRuntimeEntity);

        if (_nodeTransform == null)
        {
            _nodeTransform = transform;
        }
    }

    public PersistentEntity SpawnRuntimeEntity(EntityArchetype archetype, Vector2 worldPosition)
    {
        if (archetype == null)
            throw new ArgumentNullException(nameof(archetype));
        if (archetype.NodeData == null)
        {
            Debug.LogError(
                $"Runtime archetype '{archetype.name}' has no NodeData assigned.",
                archetype);
            return null;
        }
        if (!_persistenceRoot.RestoreCompleted)
        {
            Debug.LogWarning($"Chunk {Position} is not ready for runtime spawns.", this);
            return null;
        }

        NodeId id = NodeId.CreateRuntimeId();
        PersistentEntity entity = SpawnRuntimeNode(id, archetype.Id, archetype.NodeData, worldPosition);
        _persistenceRoot.RegisterRuntimeEntity(entity);
        return entity;
    }

    /// <summary>
    /// Checks that the chunk is restored and the node has a runtime archetype.
    /// </summary>
    public bool CanSpawnRuntimeEntity(NodeData nodeData)
    {
        return nodeData != null &&
               _persistenceRoot != null &&
               _persistenceRoot.RestoreCompleted &&
               TryGetRuntimeArchetype(nodeData, out _);
    }

    public bool ContainsRuntimeEntity(NodeData nodeData)
    {
        return nodeData != null && props.Any(node =>
            node != null &&
            node.NodeData == nodeData &&
            node.GetComponent<PersistentEntity>().PersistenceKind ==
            EntityPersistenceKind.RuntimeSpawned);
    }

    /// <summary>
    /// Spawns and registers a persistent runtime entity from its node data.
    /// </summary>
    public bool TrySpawnRuntimeEntity(
        NodeData nodeData,
        Vector2 worldPosition,
        out PersistentEntity entity)
    {
        entity = null;
        if (!CanSpawnRuntimeEntity(nodeData) ||
            !TryGetRuntimeArchetype(nodeData, out EntityArchetype archetype))
        {
            return false;
        }

        entity = SpawnRuntimeEntity(archetype, worldPosition);
        return entity != null;
    }

    // Save records store archetype IDs, so NodeData alone must resolve to one.
    private bool TryGetRuntimeArchetype(
        NodeData nodeData,
        out EntityArchetype archetype)
    {
        archetype = worldData.runtimeEntityArchetypes?
            .FirstOrDefault(candidate =>
                candidate != null && candidate.NodeData == nodeData);

        archetype ??= Resources.LoadAll<EntityArchetype>(string.Empty)
            .FirstOrDefault(candidate => candidate.NodeData == nodeData);
        return archetype != null;
    }

    private PersistentEntity RestoreRuntimeEntity(PersistentEntityRecord record)
    {
        EntityArchetype archetype = worldData.runtimeEntityArchetypes?
            .FirstOrDefault(candidate => candidate.Id == record.archetypeId);

        archetype ??= Resources.LoadAll<EntityArchetype>(string.Empty)
            .FirstOrDefault(candidate => candidate.Id == record.archetypeId);

        if (archetype == null)
        {
            Debug.LogError(
                $"Cannot restore runtime entity {record.id}: archetype ID " +
                $"{record.archetypeId} is not registered in WorldData or Resources.",
                this);
            return null;
        }

        if (archetype.NodeData == null)
        {
            Debug.LogError(
                $"Cannot restore runtime entity {record.id}: archetype " +
                $"'{archetype.name}' has no NodeData assigned.",
                archetype);
            return null;
        }

        // PersistentTransform applies the saved position immediately afterwards.
        return SpawnRuntimeNode(record.id, archetype.Id, archetype.NodeData, transform.position);
    }

    private PersistentEntity SpawnRuntimeNode(
        NodeId id,
        int archetypeId,
        NodeData nodeData,
        Vector2 worldPosition)
    {
        Vector2Int cell = Vector2Int.FloorToInt(worldPosition);
        TerrainSample sample = worldGeneration.GetTerrainSample(cell.x, cell.y);
        PropSpawnData spawnData = new()
        {
            NodeId = id,
            worldPosition = cell,
            position = worldPosition,
            scale = 1f,
            terrainSample = sample,
            persistenceKind = EntityPersistenceKind.RuntimeSpawned
        };

        Node node = nodePool.Spawn(id, spawnData, nodeData, sample, this);
        node.GetComponent<PersistentEntity>().Initialize(
            id, EntityPersistenceKind.RuntimeSpawned, archetypeId);
        node.transform.SetParent(_nodeTransform);
        props.Add(node);
        return node.GetComponent<PersistentEntity>();
    }

    private async void RestorePersistentState()
    {
        try
        {
            await dataController.RestoreChunkAsync(this);
            ApplyPersistentTileOverrides();
            WeatherSample weather = weatherService.GetRegionSample(
                WorldPartition.ChunkToRegion(Position));
            _coverage.CompleteRestore(weather);
        }
        catch (Exception)
        {
            // DataController logs the exception with the chunk as context.
        }
    }

    /// <summary>Checks whether a world cell contains the specified tile on the given layer.</summary>
    public bool HasTile(
        Vector3Int worldCell,
        PersistentTileLayer layer,
        TileData tile)
    {
        return tile != null &&
               tile.HasVisual &&
               TryGetLocalCell(worldCell, layer, out _) &&
               worldTilemapRenderer.TryGetTileData(
                   layer,
                   worldCell,
                   out TileData current) &&
               current == tile;
    }

    public bool HasTile(Vector3Int worldCell, PersistentTileLayer layer)
    {
        return TryGetLocalCell(worldCell, layer, out _) &&
               worldTilemapRenderer.HasTile(layer, worldCell);
    }

    public bool TryGetTileData(
        Vector3Int worldCell,
        PersistentTileLayer layer,
        out TileData tileData)
    {
        tileData = null;
        if (!TryGetLocalCell(worldCell, layer, out _))
            return false;

        return worldTilemapRenderer.TryGetTileData(
            layer,
            worldCell,
            out tileData);
    }

    /// <summary>
    /// Returns true when gameplay or persistence has replaced/cleared the
    /// procedurally generated tile at this cell.
    /// </summary>
    public bool IsTileChanged(
        Vector3Int worldCell,
        PersistentTileLayer layer)
    {
        return TryGetLocalCell(worldCell, layer, out Vector3Int localCell) &&
               _persistenceRoot.HasTileOverride(
                   (byte)localCell.x,
                   (byte)localCell.y,
                   layer);
    }

    /// <summary>Places a registered tile at a world cell after this chunk has restored.</summary>
    public bool TryPlaceTile(
        Vector3Int worldCell,
        PersistentTileLayer layer,
        TileData tile)
    {
        return TryPlaceTile(worldCell, layer, tile, tile != null ? tile.Color : Color.white);
    }

    /// <summary>
    /// Places a registered tile with a caller-provided tint and persists the
    /// tile identity. The tint is visual state derived from the world and does
    /// not need a separate save entry.
    /// </summary>
    public bool TryPlaceTile(
        Vector3Int worldCell,
        PersistentTileLayer layer,
        TileData tile,
        Color color)
    {
        return TryPlaceTile(
            worldCell,
            layer,
            tile,
            color,
            PersistentTileTint.TileDefault);
    }

    public bool TryPlaceTile(
        Vector3Int worldCell,
        PersistentTileLayer layer,
        TileData tile,
        Color color,
        PersistentTileTint tint)
    {
        if (!TryGetLocalCell(worldCell, layer, out Vector3Int localCell) || tile == null)
            return false;

        if (!worldData.TryGetTileData(tile.TileId, out TileData registered) ||
            registered != tile ||
            !tile.HasVisual)
        {
            Debug.LogWarning(
                $"Tile '{tile.name}' is not registered in WorldData.tiles and cannot be persisted.",
                tile);
            return false;
        }

        PersistentTileLayer targetLayer =
            tile.IsWall ? PersistentTileLayer.Wall : layer;
        worldTilemapRenderer.SetTile(
            targetLayer,
            worldCell,
            tile,
            color);
        SetPersistentTileOverride(localCell, targetLayer, tile, tint);

        if (targetLayer == PersistentTileLayer.Ground)
        {
            ushort coverageIndex = (ushort)(
                localCell.x +
                localCell.y * ChunkBuildResult.ChunkSize);
            _coverage.ClearCell(coverageIndex);
        }

        if (targetLayer == PersistentTileLayer.Wall)
        {
            worldTilemapRenderer.SetTile(
                PersistentTileLayer.Ground,
                worldCell,
                null,
                Color.white);
            SetPersistentClearOverride(localCell, PersistentTileLayer.Ground);
            ApplyLinkedCeiling(worldCell, tile);
            RefreshCoverageForGroundCell(
                localCell,
                PersistentTileLayer.Ground);
        }

        if (targetLayer != PersistentTileLayer.Ground)
            RefreshCoverageForGroundCell(localCell, targetLayer);
        return true;
    }

    /// <summary>Persists an intentionally empty tile at a world cell.</summary>
    public bool TryClearTile(Vector3Int worldCell, PersistentTileLayer layer)
    {
        if (!TryGetLocalCell(worldCell, layer, out Vector3Int localCell))
            return false;

        TileData removedWall = null;
        if (layer == PersistentTileLayer.Wall)
        {
            worldTilemapRenderer.TryGetTileData(
                PersistentTileLayer.Wall,
                worldCell,
                out removedWall);
        }

        worldTilemapRenderer.SetTile(layer, worldCell, null, Color.white);
        SetPersistentClearOverride(localCell, layer);
        if (removedWall != null && removedWall.ceilingTile != null)
        {
            worldTilemapRenderer.SetTile(
                PersistentTileLayer.Ceiling,
                worldCell,
                null,
                Color.white);
        }
        RefreshCoverageForGroundCell(localCell, layer);
        return true;
    }

    /// <summary>
    /// Replaces a mined tile with the biome override, or the world default when
    /// no biome override is configured. Clears the tile when neither is assigned.
    /// </summary>
    public bool TryReplaceMinedTile(
        Vector3Int worldCell,
        PersistentTileLayer layer)
    {
        TerrainSample sample =
            worldGeneration.GetTerrainSample(worldCell.x, worldCell.y);
        bool usesBiomeOverride =
            sample.biome != null &&
            sample.biome.overrideMinedTileReplacement != null;
        TileData replacement = usesBiomeOverride
            ? sample.biome.overrideMinedTileReplacement
            : worldData.minedTileReplacement;

        if (replacement == null)
            return TryClearTile(worldCell, layer);

        const PersistentTileTint tint = PersistentTileTint.BiomeDirt;
        if (layer == PersistentTileLayer.Wall && !replacement.IsWall)
        {
            if (!TryClearTile(worldCell, PersistentTileLayer.Wall))
                return false;
            layer = PersistentTileLayer.Ground;
        }
        return TryPlaceTile(
            worldCell,
            layer,
            replacement,
            GetTileColor(replacement, sample.biomeBlend, tint),
            tint);
    }

    /// <summary>Removes an override and restores the procedurally generated tile.</summary>
    public bool TryResetTile(Vector3Int worldCell, PersistentTileLayer layer)
    {
        if (!TryGetLocalCell(worldCell, layer, out Vector3Int localCell))
            return false;

        _persistenceRoot.RemoveTileOverride(
            (byte)localCell.x, (byte)localCell.y, layer);
        ApplyBaseline(localCell, layer);
        if (layer == PersistentTileLayer.Wall)
        {
            _persistenceRoot.RemoveTileOverride(
                (byte)localCell.x,
                (byte)localCell.y,
                PersistentTileLayer.Ground);
            ApplyBaseline(localCell, PersistentTileLayer.Ground);
            int index =
                localCell.x + localCell.y * ChunkBuildResult.ChunkSize;
            ApplyLinkedCeiling(
                worldCell,
                _baselineTiles[(int)PersistentTileLayer.Wall][index]);
        }
        RefreshCoverageForGroundCell(localCell, layer);
        return true;
    }

    private void ApplyPersistentTileOverrides()
    {
        foreach (TileOverrideData tileOverride in _persistenceRoot.TileOverrides)
        {
            Vector3Int localCell = new(tileOverride.localX, tileOverride.localY);
            Vector3Int worldCell = LocalToWorldCell(localCell);

            if (tileOverride.kind == TileOverrideKind.Clear)
            {
                worldTilemapRenderer.SetTile(
                    tileOverride.layer,
                    worldCell,
                    null,
                    Color.white);
                continue;
            }

            if (!worldData.TryGetTileData(
                    tileOverride.tileId,
                    out TileData tileData) ||
                !tileData.HasVisual)
            {
                Debug.LogWarning(
                    $"Chunk {Position} references missing persistent tile ID {tileOverride.tileId}.",
                    this);
                continue;
            }

            TerrainSample sample = worldGeneration.GetTerrainSample(
                worldCell.x,
                worldCell.y);
            Color color = GetTileColor(
                tileData,
                sample.biomeBlend,
                tileOverride.tint);
            PersistentTileLayer restoredLayer = tileData.IsWall
                ? PersistentTileLayer.Wall
                : tileOverride.layer;
            worldTilemapRenderer.SetTile(
                restoredLayer,
                worldCell,
                tileData,
                color);
            if (restoredLayer == PersistentTileLayer.Wall)
            {
                worldTilemapRenderer.SetTile(
                    PersistentTileLayer.Ground,
                    worldCell,
                    null,
                    Color.white);
            }
        }

        RebuildLinkedCeilings();
    }

    private void SetPersistentTileOverride(
        Vector3Int localCell,
        PersistentTileLayer layer,
        TileData tile,
        PersistentTileTint tint)
    {
        _persistenceRoot.SetTileOverride(new TileOverrideData
        {
            localX = (byte)localCell.x,
            localY = (byte)localCell.y,
            layer = layer,
            kind = TileOverrideKind.Place,
            tileId = tile.TileId,
            tint = tint
        });
    }

    private void SetPersistentClearOverride(
        Vector3Int localCell,
        PersistentTileLayer layer)
    {
        _persistenceRoot.SetTileOverride(new TileOverrideData
        {
            localX = (byte)localCell.x,
            localY = (byte)localCell.y,
            layer = layer,
            kind = TileOverrideKind.Clear,
            tileId = -1
        });
    }

    private void ApplyLinkedCeiling(Vector3Int worldCell, TileData wall)
    {
        TileData ceiling = wall != null ? wall.ceilingTile : null;
        worldTilemapRenderer.SetTile(
            PersistentTileLayer.Ceiling,
            worldCell,
            ceiling,
            ceiling != null ? ceiling.Color : Color.white);
    }

    private void RebuildLinkedCeilings()
    {
        for (int y = 0; y < ChunkBuildResult.ChunkSize; y++)
        {
            for (int x = 0; x < ChunkBuildResult.ChunkSize; x++)
            {
                Vector3Int worldCell = LocalToWorldCell(new Vector3Int(x, y));
                worldTilemapRenderer.TryGetTileData(
                    PersistentTileLayer.Wall,
                    worldCell,
                    out TileData wall);
                ApplyLinkedCeiling(worldCell, wall);
            }
        }
    }

    private void OnDestroy()
    {
        foreach (Tile tile in _coverageTiles.Values)
        {
            if (tile == null)
                continue;

            if (Application.isPlaying)
                Destroy(tile);
            else
                DestroyImmediate(tile);
        }

        _coverageTiles.Clear();
    }

    public void AdvanceCoverage(
        WeatherSample weather,
        long elapsedTicks) =>
        _coverage.Advance(weather, elapsedTicks);

    internal int CoverageGeneration => _coverage?.Generation ?? -1;

    public bool TryGetCoverage(
        Vector3Int worldCell,
        CoverageData coverage,
        out float amount)
    {
        amount = 0f;
        return TryGetCoverageIndex(worldCell, out ushort index) &&
               _coverage.TryGetCoverage(index, coverage, out amount);
    }

    internal bool TryGetNeighborCoverageSeed(
        ushort localIndex,
        CoverageData coverage,
        out float amount)
    {
        amount = 0f;
        if (coverage == null || chunkloader == null)
            return false;

        int size = ChunkBuildResult.ChunkSize;
        int localX = localIndex % size;
        int localY = localIndex / size;
        float total = 0f;
        int count = 0;

        for (int chunkY = -1; chunkY <= 1; chunkY++)
        {
            for (int chunkX = -1; chunkX <= 1; chunkX++)
            {
                if (chunkX == 0 && chunkY == 0)
                    continue;

                Vector2Int neighborPosition =
                    Position + new Vector2Int(chunkX, chunkY);
                int sampleX = chunkX < 0
                    ? size - 1
                    : chunkX > 0
                        ? 0
                        : localX;
                int sampleY = chunkY < 0
                    ? size - 1
                    : chunkY > 0
                        ? 0
                        : localY;
                Vector3Int worldCell = new(
                    neighborPosition.x * size + sampleX,
                    neighborPosition.y * size + sampleY);

                if (!chunkloader.TryGetLoadedChunk(
                        worldCell,
                        out Chunk neighbor) ||
                    neighbor == this ||
                    !neighbor.TryGetCoverage(
                        worldCell,
                        coverage,
                        out float neighborAmount))
                {
                    continue;
                }

                total += neighborAmount;
                count++;
            }
        }

        if (count == 0)
            return false;

        amount = Mathf.Clamp01(total / count);
        return true;
    }

    public bool TrySetCoverage(
        Vector3Int worldCell,
        CoverageData coverage,
        float amount)
    {
        return TryGetCoverageIndex(worldCell, out ushort index) &&
               _coverage.TrySetCoverage(index, coverage, amount);
    }

    internal void ApplyCoverageVisual(
        ushort localIndex,
        CoverageData coverage,
        float amount)
    {
        EnsureTilemaps();
        Vector3Int localCell = new(
            localIndex % ChunkBuildResult.ChunkSize,
            localIndex / ChunkBuildResult.ChunkSize);
        Vector3Int worldCell = LocalToWorldCell(localCell);

        if (coverage == null ||
            coverage.CoverageSprite == null ||
            amount <= 0f ||
            !worldTilemapRenderer.HasTile(
                PersistentTileLayer.Ground,
                worldCell))
        {
            _coverageTilemap.SetTile(localCell, null);
            return;
        }

        if (!_coverageTiles.TryGetValue(coverage, out Tile tile))
        {
            tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = $"{coverage.name} Runtime Coverage Tile";
            tile.sprite = coverage.CoverageSprite;
            tile.color = Color.white;
            tile.flags = TileFlags.None;
            tile.hideFlags = HideFlags.HideAndDontSave;
            _coverageTiles.Add(coverage, tile);
        }

        _coverageTilemap.SetTile(localCell, tile);
        _coverageTilemap.SetTileFlags(localCell, TileFlags.None);
        Color color = GetCoverageColor(coverage, localIndex);
        color.a *= Mathf.Clamp01(amount);
        _coverageTilemap.SetColor(localCell, color);
    }

    internal void ApplyCoverageVisuals(
        ushort[] localIndices,
        int count,
        CoverageData[] coverageByCell,
        byte[] alphaByCell)
    {
        if (localIndices == null ||
            coverageByCell == null ||
            alphaByCell == null ||
            count <= 0)
        {
            return;
        }

        EnsureTilemaps();
        TileChangeData[] changes = new TileChangeData[count];
        for (int i = 0; i < count; i++)
        {
            ushort localIndex = localIndices[i];
            Vector3Int localCell = new(
                localIndex % ChunkBuildResult.ChunkSize,
                localIndex / ChunkBuildResult.ChunkSize);
            Vector3Int worldCell = LocalToWorldCell(localCell);
            CoverageData coverage = coverageByCell[localIndex];
            float amount = alphaByCell[localIndex] / 255f;
            TileBase tile = null;
            Color color = Color.white;

            if (coverage != null &&
                coverage.CoverageSprite != null &&
                amount > 0f &&
                worldTilemapRenderer.HasTile(
                    PersistentTileLayer.Ground,
                    worldCell))
            {
                if (!_coverageTiles.TryGetValue(coverage, out Tile runtimeTile))
                {
                    runtimeTile = ScriptableObject.CreateInstance<Tile>();
                    runtimeTile.name =
                        $"{coverage.name} Runtime Coverage Tile";
                    runtimeTile.sprite = coverage.CoverageSprite;
                    runtimeTile.color = Color.white;
                    runtimeTile.flags = TileFlags.None;
                    runtimeTile.hideFlags = HideFlags.HideAndDontSave;
                    _coverageTiles.Add(coverage, runtimeTile);
                }

                tile = runtimeTile;
                color = GetCoverageColor(coverage, localIndex);
                color.a *= amount;
            }

            changes[i] = new TileChangeData(
                localCell,
                tile,
                color,
                Matrix4x4.identity);
        }

        _coverageTilemap.SetTiles(changes, ignoreLockFlags: true);
    }

    internal bool TryGetCoverageGroundTile(
        ushort localIndex,
        out TileData tile)
    {
        tile = null;
        if (localIndex >=
            ChunkBuildResult.ChunkSize * ChunkBuildResult.ChunkSize)
        {
            return false;
        }

        Vector3Int localCell = new(
            localIndex % ChunkBuildResult.ChunkSize,
            localIndex / ChunkBuildResult.ChunkSize);
        return worldTilemapRenderer.TryGetTileData(
            PersistentTileLayer.Ground,
            LocalToWorldCell(localCell),
            out tile);
    }

    private Color GetCoverageColor(
        CoverageData coverage,
        ushort localIndex)
    {
        Color configured = coverage.CoverageColor;
        if (coverage.ColorSource == CoverageColorSource.CoverageColor ||
            localIndex >= _biomeBlends.Length)
        {
            return configured;
        }

        BiomeBlend biome = _biomeBlends[localIndex];
        Color color = coverage.ColorSource switch
        {
            CoverageColorSource.BiomeGround => biome.groundColor,
            CoverageColorSource.BiomeSand => biome.beachColor,
            CoverageColorSource.BiomeWater => biome.waterColor,
            CoverageColorSource.BiomeCliff => biome.cliffColor,
            CoverageColorSource.BiomePath => biome.pathColor,
            _ => configured
        };
        color.a *= configured.a;
        return color;
    }

    internal void ApplyCoverageMaterial(
        CoverageData coverage,
        float _)
    {
        EnsureTilemaps();
        TilemapRenderer renderer =
            _coverageTilemap.GetComponent<TilemapRenderer>();
        _coveragePropertyBlock ??= new MaterialPropertyBlock();
        renderer.GetPropertyBlock(_coveragePropertyBlock);

        if (coverage == null || coverage.CoverageSprite == null)
        {
            //renderer.SetPropertyBlock(null);
        }
        else
        {
            _coveragePropertyBlock.SetVector(
                CoverageTilingId,
                new Vector4(
                    coverage.CoverageTiling.x,
                    coverage.CoverageTiling.y,
                    0f,
                    0f));
        }

        _coveragePropertyBlock.SetVector(
            WorldOffsetId,
            new Vector4(
                Position.x * ChunkBuildResult.ChunkSize,
                Position.y * ChunkBuildResult.ChunkSize,
                0f,
                0f));
        renderer.SetPropertyBlock(_coveragePropertyBlock);
    }

    internal void ClearCoverageVisuals()
    {
        if (_coverageTilemap != null)
            _coverageTilemap.ClearAllTiles();

        ApplyCoverageMaterial(null, 0f);
    }

    private bool TryGetCoverageIndex(
        Vector3Int worldCell,
        out ushort index)
    {
        index = 0;
        if (!_persistenceRoot.RestoreCompleted)
            return false;

        int localX =
            worldCell.x - Position.x * ChunkBuildResult.ChunkSize;
        int localY =
            worldCell.y - Position.y * ChunkBuildResult.ChunkSize;
        if (localX < 0 ||
            localY < 0 ||
            localX >= ChunkBuildResult.ChunkSize ||
            localY >= ChunkBuildResult.ChunkSize)
        {
            return false;
        }

        index = (ushort)(localX + localY * ChunkBuildResult.ChunkSize);
        return true;
    }

    private void RefreshCoverageForGroundCell(
        Vector3Int localCell,
        PersistentTileLayer layer)
    {
        if (layer != PersistentTileLayer.Ground ||
            _coverage == null ||
            !_coverage.IsReady)
        {
            return;
        }

        ushort index = (ushort)(
            localCell.x +
            localCell.y * ChunkBuildResult.ChunkSize);
        _coverage.RefreshCell(index);
    }

    /// <summary>
    /// Applies a complete prebaked layer using chunk-local cell coordinates.
    /// Neighbor evaluation has already happened in the world bake coordinator.
    /// </summary>
    public void ApplyBakedTiles(
        PersistentTileLayer layer,
        TileChangeData[] changes)
    {
        EnsureTilemaps();
        Tilemap tilemap = GetTilemap(layer);
        // Apply identity, color, flags, and transform in one native batch.
        // The previous SetTilesBlock + three calls per populated cell made this
        // the dominant main-thread cost while streaming chunks.
        tilemap.SetTiles(changes, ignoreLockFlags: true);
    }

    /// <summary>Applies one prebaked visual cell in chunk-local coordinates.</summary>
    public void ApplyBakedTile(
        PersistentTileLayer layer,
        TileChangeData change)
    {
        EnsureTilemaps();
        Tilemap tilemap = GetTilemap(layer);
        tilemap.SetTile(change, ignoreLockFlags: true);
    }

    public void SetBakedColor(
        PersistentTileLayer layer,
        Vector3Int localCell,
        Color color)
    {
        EnsureTilemaps();
        Tilemap tilemap = GetTilemap(layer);
        TileBase tile = tilemap.GetTile(localCell);
        if (tile == null)
            return;

        TileChangeData change = new(
            localCell,
            tile,
            color,
            tilemap.GetTransformMatrix(localCell));
        tilemap.SetTile(change, ignoreLockFlags: true);
    }

    public void ClearBakedTiles()
    {
        if (_groundTilemap != null)
            _groundTilemap.ClearAllTiles();
        if (_wallTilemap != null)
            _wallTilemap.ClearAllTiles();
        if (_ceilingTilemap != null)
            _ceilingTilemap.ClearAllTiles();
        if (_waterTilemap != null)
            _waterTilemap.ClearAllTiles();
        if (_coverageTilemap != null)
            _coverageTilemap.ClearAllTiles();
    }

    public Tilemap GetTilemap(PersistentTileLayer layer)
    {
        EnsureTilemaps();
        return layer switch
        {
            PersistentTileLayer.Ground => _groundTilemap,
            PersistentTileLayer.Water => _waterTilemap,
            PersistentTileLayer.Wall => _wallTilemap,
            PersistentTileLayer.Ceiling => _ceilingTilemap,
            _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, null)
        };
    }

    private void EnsureTilemaps()
    {
        if (_groundTilemap == null)
        {
            _groundTilemap = CreateTilemap(
                "Ground",
                0,
                -4,
                addCollider: false);
        }

        if (_wallTilemap == null)
        {
            _wallTilemap = CreateTilemap(
                "Wall",
                0,
                -2,
                addCollider: true);
        }

        if (_ceilingTilemap == null)
        {
            _ceilingTilemap = CreateTilemap(
                "Ceiling",
                0,
                1,
                offset: new Vector2Int(0,1));
        }

        if (_waterTilemap == null)
        {
            _waterTilemap = CreateTilemap(
                "Water",
                LayerMask.NameToLayer("Water"),
                -1,
                addCollider: false);
        }

        if (_coverageTilemap == null)
        {
            _coverageTilemap = CreateTilemap(
                "Coverage",
                0,
                -3,
                addCollider: false,
                drawIndiviual: false);
        }

        if (_tilemapsConfigured)
            return;

        worldTilemapRenderer.ConfigureChunkTilemap(
            PersistentTileLayer.Ground,
            _groundTilemap.GetComponent<TilemapRenderer>(),
            _groundTilemap.GetComponent<TilemapCollider2D>());
        worldTilemapRenderer.ConfigureChunkTilemap(
            PersistentTileLayer.Wall,
            _wallTilemap.GetComponent<TilemapRenderer>(),
            _wallTilemap.GetComponent<TilemapCollider2D>());
        worldTilemapRenderer.ConfigureChunkTilemap(
            PersistentTileLayer.Ceiling,
            _ceilingTilemap.GetComponent<TilemapRenderer>(),
            _ceilingTilemap.GetComponent<TilemapCollider2D>());
        worldTilemapRenderer.ConfigureChunkTilemap(
            PersistentTileLayer.Water,
            _waterTilemap.GetComponent<TilemapRenderer>(),
            _waterTilemap.GetComponent<TilemapCollider2D>());
        worldTilemapRenderer.ConfigureCoverageTilemap(
            _coverageTilemap.GetComponent<TilemapRenderer>());
        _tilemapsConfigured = true;
    }

    private Tilemap CreateTilemap(
        string layerName,
        int layer,
        int sortingOrder,
        bool addCollider = false,
        Vector2Int offset = default,
        bool drawIndiviual = false)
    {
        GameObject layerObject = new(layerName);
        layerObject.layer = layer >= 0 ? layer : 0;
        layerObject.transform.SetParent(transform, false);
        layerObject.transform.position += new Vector3(offset.x, offset.y, 0);

        Tilemap tilemap = layerObject.AddComponent<Tilemap>();
        TilemapRenderer renderer = layerObject.AddComponent<TilemapRenderer>();
        renderer.sortingOrder = sortingOrder;
        renderer.mode = drawIndiviual ? TilemapRenderer.Mode.Individual : TilemapRenderer.Mode.Chunk;

        if (addCollider)
            layerObject.AddComponent<TilemapCollider2D>();
        return tilemap;
    }

    /// <summary>
    /// Recomputes only the visual colors of this chunk from the current
    /// seasonal biome blends. Tile identities and persistent state are left
    /// unchanged.
    /// </summary>
    public void BeginBiomeColorRefresh(int hour)
    {
        if (worldGeneration.Preset.heightMapDebug)
        {
            _biomeColorRefreshIndex = -1;
            return;
        }

        _biomeColorRefreshCount = 0;
        int offsetX = Position.x * ChunkBuildResult.ChunkSize;
        int offsetY = Position.y * ChunkBuildResult.ChunkSize;
        int dayKey = SeasonalBiomeTint.GetDayKey(
            timeController.DayInMonth,
            timeController.Season,
            timeController.Year);
        for (int index = 0; index < _biomeColorRefreshOrder.Length; index++)
        {
            int x = index % ChunkBuildResult.ChunkSize;
            int y = index / ChunkBuildResult.ChunkSize;
            int scheduledHour = SeasonalBiomeTint.GetScheduledHour(
                    offsetX + x,
                    offsetY + y,
                    timeController.DayInMonth,
                    timeController.Season,
                    timeController.Year);
            if (scheduledHour <= hour &&
                _biomeColorAppliedDayKeys[index] != dayKey)
            {
                _biomeColorRefreshOrder[_biomeColorRefreshCount++] = index;
            }
        }

        int seed = unchecked(
            Position.x * 73856093 ^
            Position.y * 19349663 ^
            timeController.DayInMonth * 83492791 ^
            (int)timeController.Season * 486187739 ^
            timeController.Year);
        System.Random random = new(seed);
        for (int i = _biomeColorRefreshCount - 1; i > 0; i--)
        {
            int swapIndex = random.Next(i + 1);
            (_biomeColorRefreshOrder[i], _biomeColorRefreshOrder[swapIndex]) =
                (_biomeColorRefreshOrder[swapIndex], _biomeColorRefreshOrder[i]);
        }

        _biomeColorRefreshIndex = 0;
    }

    /// <summary>
    /// Applies up to <paramref name="cellBudget"/> cells of a pending biome
    /// color refresh. Returns true once this chunk has finished refreshing.
    /// Unity Tilemap writes remain on the main thread, but are spread over
    /// multiple frames by Chunkloader.
    /// </summary>
    public bool RefreshBiomeColorCells(int cellBudget)
    {
        if (_biomeColorRefreshIndex < 0)
            return true;

        int endIndex = Mathf.Min(
            _biomeColorRefreshIndex + Mathf.Max(1, cellBudget),
            _biomeColorRefreshCount);

        while (_biomeColorRefreshIndex < endIndex)
        {
            int index = _biomeColorRefreshOrder[_biomeColorRefreshIndex++];
            int x = index % ChunkBuildResult.ChunkSize;
            int y = index / ChunkBuildResult.ChunkSize;
            Vector3Int localCell = new(x, y);
            BiomeBlend biome = _biomeBlends[index];
            int worldX = Position.x * ChunkBuildResult.ChunkSize + x;
            int worldY = Position.y * ChunkBuildResult.ChunkSize + y;
            SeasonalBiomeTint.Reapply(
                ref biome,
                worldData,
                timeController,
                worldX,
                worldY);
            _biomeBlends[index] = biome;
            _biomeColorAppliedDayKeys[index] = SeasonalBiomeTint.GetDayKey(
                timeController.DayInMonth,
                timeController.Season,
                timeController.Year);

            RefreshBaselineColor(
                localCell,
                index,
                PersistentTileLayer.Ground,
                biome);
            RefreshBaselineColor(
                localCell,
                index,
                PersistentTileLayer.Wall,
                biome);
            if (_coverage != null && _coverage.IsReady)
                _coverage.RefreshCellColor((ushort)index);
        }

        if (_biomeColorRefreshIndex < _biomeColorRefreshCount)
            return false;

        _biomeColorRefreshIndex = -1;
        if (_persistenceRoot.RestoreCompleted)
            ApplyPersistentTileOverrides();
        return true;
    }

    private void RefreshBaselineColor(
        Vector3Int localCell,
        int index,
        PersistentTileLayer layer,
        BiomeBlend biome)
    {
        TileData tile = _baselineTiles[(int)layer][index];
        if (tile == null)
            return;

        Color color = GetTileColor(
            tile,
            biome,
            _baselineTints[(int)layer][index]);
        color *= tile.Color;
        _baselineColors[(int)layer][index] = color;

        if (!_persistenceRoot.HasTileOverride(
                (byte)localCell.x,
                (byte)localCell.y,
                layer))
        {
            worldTilemapRenderer.SetColor(
                layer,
                LocalToWorldCell(localCell),
                color);
        }
    }

    private static Color GetTileColor(
        TileData tile,
        BiomeBlend biome,
        PersistentTileTint tint)
    {
        return tint switch
        {
            PersistentTileTint.BiomeGround => biome.groundColor,
            PersistentTileTint.BiomeDirt => biome.dirtColor,
            PersistentTileTint.BiomePath => biome.pathColor,
            PersistentTileTint.BiomeWater => biome.waterColor,
            PersistentTileTint.BiomeCliff => biome.cliffColor,
            PersistentTileTint.BiomeBeach => biome.beachColor,
            _ => tile.Color
        };
    }

    private PersistentTileTint GetGroundTint(float height, bool isCliff)
    {
        if (height < worldGeneration.Elevation.waterHeight)
            return PersistentTileTint.BiomeBeach;

        if (isCliff)
            return PersistentTileTint.BiomeCliff;

        if (height < worldGeneration.Elevation.beachHeight)
            return PersistentTileTint.BiomeBeach;

        return PersistentTileTint.BiomeGround;
    }

    private bool TryGetLocalCell(
        Vector3Int worldCell,
        PersistentTileLayer layer,
        out Vector3Int localCell)
    {
        localCell = new Vector3Int(
            worldCell.x - Position.x * ChunkBuildResult.ChunkSize,
            worldCell.y - Position.y * ChunkBuildResult.ChunkSize,
            0);

        return _persistenceRoot.RestoreCompleted &&
               Enum.IsDefined(typeof(PersistentTileLayer), layer) &&
               localCell.x >= 0 &&
               localCell.y >= 0 &&
               localCell.x < ChunkBuildResult.ChunkSize &&
               localCell.y < ChunkBuildResult.ChunkSize;
    }

    private void ApplyBaseline(Vector3Int localCell, PersistentTileLayer layer)
    {
        int index = localCell.x + localCell.y * ChunkBuildResult.ChunkSize;
        TileData tileData = _baselineTiles[(int)layer][index];
        worldTilemapRenderer.SetTile(
            layer,
            LocalToWorldCell(localCell),
            tileData,
            _baselineColors[(int)layer][index]);
    }

    private Vector3Int LocalToWorldCell(Vector3Int localCell)
    {
        return new Vector3Int(
            Position.x * ChunkBuildResult.ChunkSize + localCell.x,
            Position.y * ChunkBuildResult.ChunkSize + localCell.y,
            0);
    }

    private TileData GetTile(int x, int y, BiomeBlend biome, float height, float moisture, float temperature,
        bool isCliff, out Color color)
    {
        if (worldGeneration.Preset.heightMapDebug)
        {
            if (worldGeneration.Preset.previewLayer ==
                WorldGenerationPreviewLayer.Moisture)
            {
                color = new Color(temperature, moisture, isCliff ? 0 : 1);
            }
            else
            {
                color = new Color(height, height, height);
            }

            return DebugTile;
        }

        if (height < worldGeneration.Elevation.waterHeight)
        {
            color = biome.beachColor;
            return biome.dominantBiome.overrideBeachTile ?? BeachTile;
        }
        else if (isCliff)
        {
            color = biome.cliffColor;
            return biome.dominantBiome.overrideCliffTile ?? CliffTile;
        }
        else if (height < worldGeneration.Elevation.beachHeight)
        {
            color = biome.beachColor;
            return biome.dominantBiome.overrideBeachTile ?? BeachTile;
        }
        else
        {
            color = biome.groundColor;
            return biome.dominantBiome.overrideGroundTile ?? GroundTile;
        }
    }

    // Update is called once per frame
    void Update()
    {
    }

    public class Pool : MonoMemoryPool<ChunkBuildResult, Chunk>
    {
        protected override void Reinitialize(ChunkBuildResult result, Chunk item)
        {
            item.Init(result);
        }

        protected override void OnDespawned(Chunk item)
        {
            item.dataController.CaptureBeforeUnload(item);
            item.dataController.RequestSave();
            item._coverage.PrepareForPool();
            item.UnloadProps();
            base.OnDespawned(item);
        }
    }
}
