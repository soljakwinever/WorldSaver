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

        var unique = data.props.Select(t => t.propName).Distinct().ToList();

        foreach (var propName in unique)
        {
            Debug.Log($"Valid Prop Name: {propName}");
        }

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

                var tileData = GetTile(offsetX + x, offsetY + y, biome, height, moisture, temperature, isCliff,
                    out var color);
                color *= tileData.Color;
                changes.Add(new TileChangeData(
                    tilePosition, tileData.TileBase, color, Matrix4x4.identity));
                int tileIndex = data.GetTileIndex(x, y);
                _baselineTiles[(int)PersistentTileLayer.Ground][tileIndex] = tileData;
                _baselineColors[(int)PersistentTileLayer.Ground][tileIndex] = color;
                _baselineTiles[(int)PersistentTileLayer.Water][tileIndex] = null;
                _baselineColors[(int)PersistentTileLayer.Water][tileIndex] = Color.white;

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

    /// <summary>Places a registered tile at a world cell after this chunk has restored.</summary>
    public bool TryPlaceTile(
        Vector3Int worldCell,
        PersistentTileLayer layer,
        TileData tile)
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

        GetTilemap(layer).SetTile(localCell, tile.TileBase);
        GetTilemap(layer).SetColor(localCell, tile.Color);
        _persistenceRoot.SetTileOverride(new TileOverrideData
        {
            localX = (byte)localCell.x,
            localY = (byte)localCell.y,
            layer = layer,
            kind = TileOverrideKind.Place,
            tileId = tile.TileId
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
            tilemap.SetColor(localCell, tileData.Color);
        }
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
