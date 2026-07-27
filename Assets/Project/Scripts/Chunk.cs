using System;
using System.Collections.Generic;
using System.Linq;
using Project.Scripts;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Tilemaps;
using Zenject;
using TileData = Project.Scripts.DataTypes.TileData;

[RequireComponent(typeof(ChunkPersistenceRoot))]
public class Chunk : MonoBehaviour, IChunk
{
    [Header("Tilemaps")]
    [SerializeField] private Tilemap _groundTilemap;
    [SerializeField] private Tilemap _waterTilemap;

    [Header("Transforms")]
    [SerializeField] private Transform _nodeTransform;
    
    public Vector2Int Position { get; set; }

    [Inject] private WorldData worldData;
    [Inject] private WorldGeneration worldGeneration;
    [Inject] private ITimeController timeController;

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

    readonly List<Node> props = new();
    private ChunkPersistenceRoot _persistenceRoot;
    private readonly TileData[][] _baselineTiles =
    {
        new TileData[ChunkBuildResult.ChunkSize * ChunkBuildResult.ChunkSize],
        new TileData[ChunkBuildResult.ChunkSize * ChunkBuildResult.ChunkSize]
    };
    private readonly Color[][] _baselineColors =
    {
        new Color[ChunkBuildResult.ChunkSize * ChunkBuildResult.ChunkSize],
        new Color[ChunkBuildResult.ChunkSize * ChunkBuildResult.ChunkSize]
    };
    private readonly PersistentTileTint[][] _baselineTints =
    {
        new PersistentTileTint[ChunkBuildResult.ChunkSize * ChunkBuildResult.ChunkSize],
        new PersistentTileTint[ChunkBuildResult.ChunkSize * ChunkBuildResult.ChunkSize]
    };
    private readonly BiomeBlend[] _biomeBlends =
        new BiomeBlend[ChunkBuildResult.ChunkSize * ChunkBuildResult.ChunkSize];
    private readonly int[] _biomeColorRefreshOrder =
        new int[ChunkBuildResult.ChunkSize * ChunkBuildResult.ChunkSize];
    private readonly int[] _biomeColorAppliedDayKeys =
        new int[ChunkBuildResult.ChunkSize * ChunkBuildResult.ChunkSize];
    private int _biomeColorRefreshIndex = -1;
    private int _biomeColorRefreshCount;

    public int RemainingBiomeColorRefreshCells =>
        _biomeColorRefreshIndex < 0
            ? 0
            : _biomeColorRefreshCount - _biomeColorRefreshIndex;

    public void Init(ChunkBuildResult data)
    {
        _groundTilemap.ClearAllTiles();
        _waterTilemap.ClearAllTiles();
        Position = data.chunkPosition;
        _persistenceRoot.BeginRestore(Position);
        
        this.name = $"Chunk_{Position.x},{Position.y}";

        transform.position = new Vector3(Position.x * ChunkBuildResult.ChunkSize,
            Position.y * ChunkBuildResult.ChunkSize, 0);

        int offsetX = Position.x * ChunkBuildResult.ChunkSize;
        int offsetY = Position.y * ChunkBuildResult.ChunkSize;

        List<TileChangeData> changes = new();
        List<TileChangeData> waterTiles = new();

        for (int y = 0; y < ChunkBuildResult.ChunkSize; y++)
        {
            for (int x = 0; x < ChunkBuildResult.ChunkSize; x++)
            {
                var height = data.heights[data.GetTileIndex(x, y)];
                var biome = data.biomeData[data.GetTileIndex(x, y)];
                var moisture = data.moisture[data.GetTileIndex(x, y)];
                var temperature = data.temperature[data.GetTileIndex(x, y)];
                var isCliff = data.isCliff[data.GetTileIndex(x, y)];
                var isWater = height <= worldData.waterHeight;

                Vector3Int tilePosition = new Vector3Int(x, y, 0);
                SeasonalBiomeTint.Reapply(
                    ref biome,
                    worldData,
                    timeController,
                    offsetX + x,
                    offsetY + y);

                var tileData = GetTile(offsetX + x, offsetY + y, biome, height, moisture, temperature, isCliff,
                    out var color);
                color *= tileData.Color;
                changes.Add(new TileChangeData(
                    tilePosition, tileData.TileBase, color, Matrix4x4.identity));
                int tileIndex = data.GetTileIndex(x, y);
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
                _baselineTiles[(int)PersistentTileLayer.Ground][tileIndex] = tileData;
                _baselineColors[(int)PersistentTileLayer.Ground][tileIndex] = color;
                _baselineTints[(int)PersistentTileLayer.Ground][tileIndex] =
                    GetGroundTint(height, isCliff);
                _baselineTiles[(int)PersistentTileLayer.Water][tileIndex] = null;
                _baselineColors[(int)PersistentTileLayer.Water][tileIndex] = Color.white;
                _baselineTints[(int)PersistentTileLayer.Water][tileIndex] =
                    PersistentTileTint.BiomeWater;

                if (isWater && !worldData.heightMapDebug)
                {
                    tileData = WaterTile;
                    Color waterColor = new(
                        Mathf.InverseLerp(0, worldData.waterHeight, height),
                        Mathf.InverseLerp(0, worldData.waterHeight, height),
                        Mathf.InverseLerp(0, worldData.waterHeight, height));
                    waterColor *= tileData.Color;
                    waterTiles.Add(new TileChangeData(tilePosition, tileData.TileBase, waterColor,
                        Matrix4x4.identity));
                    _baselineTiles[(int)PersistentTileLayer.Water][tileIndex] = tileData;
                    _baselineColors[(int)PersistentTileLayer.Water][tileIndex] = waterColor;
                }
            }
        }

        _groundTilemap.SetTiles(changes.ToArray(), true);

        if (worldData.heightMapDebug) return;

        _waterTilemap.SetTiles(waterTiles.ToArray(), true);

        foreach (var propSpawnData in data.props)
        {
            var rule = worldData.propSpawnRules.First(t => t.name == propSpawnData.propName);

            var prop = nodePool.Spawn(propSpawnData.NodeId, propSpawnData, rule.nodeData, propSpawnData.terrainSample,
                this);
            prop.transform.SetParent(_nodeTransform);

            props.Add(prop);
            _persistenceRoot.RegisterGeneratedEntity(
                prop.GetComponent<PersistentEntity>());
        }

        RestorePersistentState();
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
               tile.TileBase != null &&
               TryGetLocalCell(worldCell, layer, out Vector3Int localCell) &&
               GetTilemap(layer).GetTile(localCell) == tile.TileBase;
    }

    public bool HasTile(Vector3Int worldCell, PersistentTileLayer layer)
    {
        return TryGetLocalCell(worldCell, layer, out Vector3Int localCell) &&
               GetTilemap(layer).HasTile(localCell);
    }

    public bool TryGetTileData(
        Vector3Int worldCell,
        PersistentTileLayer layer,
        out TileData tileData)
    {
        tileData = null;
        if (!TryGetLocalCell(worldCell, layer, out Vector3Int localCell))
            return false;

        TileBase currentTile = GetTilemap(layer).GetTile(localCell);
        if (currentTile == null || worldData.tiles == null)
            return false;

        foreach (TileData candidate in worldData.tiles)
        {
            if (candidate != null && candidate.TileBase == currentTile)
            {
                tileData = candidate;
                return true;
            }
        }

        return false;
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
            tile.TileBase == null)
        {
            Debug.LogWarning(
                $"Tile '{tile.name}' is not registered in WorldData.tiles and cannot be persisted.",
                tile);
            return false;
        }

        Tilemap tilemap = GetTilemap(layer);
        tilemap.SetTile(localCell, tile.TileBase);
        SetTileColor(tilemap, localCell, color);
        _persistenceRoot.SetTileOverride(new TileOverrideData
        {
            localX = (byte)localCell.x,
            localY = (byte)localCell.y,
            layer = layer,
            kind = TileOverrideKind.Place,
            tileId = tile.TileId,
            tint = tint
        });
        return true;
    }

    /// <summary>Persists an intentionally empty tile at a world cell.</summary>
    public bool TryClearTile(Vector3Int worldCell, PersistentTileLayer layer)
    {
        if (!TryGetLocalCell(worldCell, layer, out Vector3Int localCell))
            return false;

        GetTilemap(layer).SetTile(localCell, null);
        _persistenceRoot.SetTileOverride(new TileOverrideData
        {
            localX = (byte)localCell.x,
            localY = (byte)localCell.y,
            layer = layer,
            kind = TileOverrideKind.Clear,
            tileId = -1
        });
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
        return true;
    }

    private void ApplyPersistentTileOverrides()
    {
        foreach (TileOverrideData tileOverride in _persistenceRoot.TileOverrides)
        {
            Vector3Int localCell = new(tileOverride.localX, tileOverride.localY);
            Tilemap tilemap = GetTilemap(tileOverride.layer);

            if (tileOverride.kind == TileOverrideKind.Clear)
            {
                tilemap.SetTile(localCell, null);
                continue;
            }

            if (!worldData.TryGetTileData(
                    tileOverride.tileId,
                    out TileData tileData) ||
                tileData.TileBase == null)
            {
                Debug.LogWarning(
                    $"Chunk {Position} references missing persistent tile ID {tileOverride.tileId}.",
                    this);
                continue;
            }

            tilemap.SetTile(localCell, tileData.TileBase);
            TerrainSample sample = worldGeneration.GetTerrainSample(
                Position.x * ChunkBuildResult.ChunkSize + localCell.x,
                Position.y * ChunkBuildResult.ChunkSize + localCell.y);
            Color color = GetTileColor(
                tileData,
                sample.biomeBlend,
                tileOverride.tint);
            SetTileColor(tilemap, localCell, color);
        }
    }

    /// <summary>
    /// Recomputes only the visual colors of this chunk from the current
    /// seasonal biome blends. Tile identities and persistent state are left
    /// unchanged.
    /// </summary>
    public void BeginBiomeColorRefresh(int hour)
    {
        if (worldData.heightMapDebug)
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
                PersistentTileLayer.Water,
                biome);
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
            SetTileColor(GetTilemap(layer), localCell, color);
        }
    }

    private static void SetTileColor(
        Tilemap tilemap,
        Vector3Int cell,
        Color color)
    {
        TileFlags flags = tilemap.GetTileFlags(cell);
        tilemap.SetTileFlags(cell, flags & ~TileFlags.LockColor);
        tilemap.SetColor(cell, color);
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
        if (height < worldData.waterHeight)
            return PersistentTileTint.BiomeBeach;

        if (isCliff)
            return PersistentTileTint.BiomeCliff;

        if (height < worldData.beachHeight)
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

    private Tilemap GetTilemap(PersistentTileLayer layer)
    {
        return layer == PersistentTileLayer.Ground ? _groundTilemap : _waterTilemap;
    }

    private void ApplyBaseline(Vector3Int localCell, PersistentTileLayer layer)
    {
        int index = localCell.x + localCell.y * ChunkBuildResult.ChunkSize;
        Tilemap tilemap = GetTilemap(layer);
        TileData tileData = _baselineTiles[(int)layer][index];
        tilemap.SetTile(localCell, tileData != null ? tileData.TileBase : null);
        tilemap.SetColor(localCell, _baselineColors[(int)layer][index]);
    }

    private TileData GetTile(int x, int y, BiomeBlend biome, float height, float moisture, float temperature,
        bool isCliff, out Color color)
    {
        if (worldData.heightMapDebug)
        {
            if (worldData.previewNoiseLayer == WorldData.NoiseLayer.Moisture)
            {
                color = new Color(temperature, moisture, isCliff ? 0 : 1);
            }
            else
            {
                color = new Color(height, height, height);
            }

            return DebugTile;
        }

        if (height < worldData.waterHeight)
        {
            color = biome.beachColor;
            return biome.dominantBiome.overrideBeachTile ?? BeachTile;
        }
        else if (isCliff)
        {
            color = biome.cliffColor;
            return biome.dominantBiome.overrideCliffTile ?? CliffTile;
        }
        else if (height < worldData.beachHeight)
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
            item.UnloadProps();
            base.OnDespawned(item);
        }
    }
}
