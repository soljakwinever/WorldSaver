using System;
using System.Collections.Generic;
using System.Linq;
using Project.Scripts;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Tilemaps;
using Zenject;

[RequireComponent(typeof(Tilemap))]
public class Chunk : MonoBehaviour
{
    private Tilemap _tilemap;
    
    public Vector2Int Position;
    
    public const int ChunkSize = 32;
    
    [Inject] private WorldData worldData;
    
    public TileBase GroundTile;
    public TileBase PathTile;
    public TileBase WaterTile;
    public TileBase CliffTile;
    public TileBase BeachTile;
    public TileBase WallTile;
    
    [Inject] private Node.Pool nodePool;
    
    readonly List<Node> props = new();
    
    private void Init(ChunkGenerator.ChunkBuildResult data)
    {
        _tilemap.ClearAllTiles();
        Position = data.chunkPosition;
        
        transform.position = new Vector3(Position.x * ChunkSize, Position.y * ChunkSize, 0);
        
        int offsetX = Position.x * ChunkSize;
        int offsetY = Position.y * ChunkSize;

        List<TileChangeData> changes = new ();
        
        var unique = data.props.Select(t=>t.propName).Distinct().ToList();

        foreach (var propName in unique)
        {
            Debug.Log($"Valid Prop Name: {propName}");
        }
        
        for (int y = 0; y < ChunkSize; y++)
        {
            for (int x = 0; x < ChunkSize; x++)
            {
                var height = data.heights[data.GetTileIndex(x, y)];
                var biome = data.biomeData[data.GetTileIndex(x, y)];
                var moisture = data.moisture[data.GetTileIndex(x, y)];
                var temperature = data.temperature[data.GetTileIndex(x, y)];
                var isCliff = data.isCliff[data.GetTileIndex(x, y)];
                
                Vector3Int tilePosition = new Vector3Int(x, y, 0);

                var tileData = GetTile(offsetX +x,offsetY +y,biome, height, moisture, temperature, isCliff, out var color);
                
                changes.Add(new TileChangeData(tilePosition, tileData, color, Matrix4x4.identity));
            }
        }
        
        _tilemap.SetTiles(changes.ToArray(), true);

        foreach (var propSpawnData in data.props)
        {
            var rule = worldData.propSpawnRules.First(t=>t.name == propSpawnData.propName);

            props.Add(nodePool.Spawn(propSpawnData, rule.nodeData));
        }
    }
    
    private void UnloadProps()
    {
        foreach (var prop in props)
        {
            nodePool.Despawn(prop);
        }
        props.Clear();
    }

    [Inject] private WorldGeneration _worldGeneration;
    [Inject] private WorldData _worldData;
    
    private void Awake()
    {
        _tilemap = GetComponent<Tilemap>();
        if(_tilemap == null)
            throw new Exception("Tilemap not found");
    }

    private TileBase GetTile(int x, int y, BiomeSelector.BiomeBlend biome, float height, float moisture, float temperature, bool isCliff, out Color color)
    {
        if (worldData.heightMapDebug)
        {
            color = new Color(height, height, height);
            return GroundTile;
        }

        if (height < worldData.waterHeight)
        {
            color = biome.waterColor;
            return WaterTile;
        }
        else if (height < worldData.beachHeight)
        {
            color = biome.beachColor;
            return BeachTile;
        }
        else if (isCliff || height > worldData.mountainHeight)
        {
            color = biome.cliffColor;
            return CliffTile;
        }
        else
        {
            color = biome.groundColor;
            return GroundTile;
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    
    public class Pool : MonoMemoryPool<ChunkGenerator.ChunkBuildResult, Chunk>
    {
        protected override void Reinitialize(ChunkGenerator.ChunkBuildResult result, Chunk item)
        {
            item.Init(result);
        }

        protected override void OnDespawned(Chunk item)
        {
            item.UnloadProps();
            base.OnDespawned(item);
        }
    }
}
