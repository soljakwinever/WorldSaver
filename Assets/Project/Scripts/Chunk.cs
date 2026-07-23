using System;
using System.Collections.Generic;
using System.Linq;
using Project.Scripts;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Tilemaps;
using Zenject;

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

    [Header("Debug")]
    public TileBase DebugTile;

    [Header("Default Tiles")]
    public TileBase GroundTile;
    public TileBase PathTile;
    public TileBase WaterTile;
    public TileBase CliffTile;
    public TileBase BeachTile;
    public TileBase WallTile;

    [Inject] private Node.Pool nodePool;
    [Inject] private DataController dataController;

    readonly List<Node> props = new();
    private ChunkPersistenceRoot _persistenceRoot;

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
                changes.Add(new TileChangeData(tilePosition, tileData, color, Matrix4x4.identity));

                if (isWater && !worldData.heightMapDebug)
                {
                    tileData = WaterTile;
                    float depth = Mathf.InverseLerp(0, worldData.waterHeight, height);
                    waterTiles.Add(new TileChangeData(tilePosition, tileData, new Color(depth, depth, depth),
                        Matrix4x4.identity));
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

        if (_nodeTransform == null)
        {
            _nodeTransform = transform;
        }
    }

    private async void RestorePersistentState()
    {
        try
        {
            await dataController.RestoreChunkAsync(this);
        }
        catch (Exception)
        {
            // DataController logs the exception with the chunk as context.
        }
    }

    private TileBase GetTile(int x, int y, BiomeBlend biome, float height, float moisture, float temperature,
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
