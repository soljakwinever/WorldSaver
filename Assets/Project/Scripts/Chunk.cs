using System;
using System.Collections.Generic;
using System.Linq;
using Project.Scripts;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Tilemaps;
using Zenject;

public class Chunk : MonoBehaviour, IChunk
{
    [SerializeField] private Tilemap _groundTilemap;
    [SerializeField] private Tilemap _waterTilemap;
    
    public Vector2Int Position { get; set; }
    
    [Inject] private WorldData worldData;

    public TileBase DebugTile;
    
    public TileBase GroundTile;
    public TileBase PathTile;
    public TileBase WaterTile;
    public TileBase CliffTile;
    public TileBase BeachTile;
    public TileBase WallTile;
    
    [Inject] private Node.Pool nodePool;
    
    readonly List<Node> props = new();
    
    public void Init(ChunkBuildResult data)
    {
        _groundTilemap.ClearAllTiles();
        _waterTilemap.ClearAllTiles();
        Position = data.chunkPosition;
        
        transform.position = new Vector3(Position.x * ChunkBuildResult.ChunkSize, Position.y * ChunkBuildResult.ChunkSize, 0);
        
        int offsetX = Position.x * ChunkBuildResult.ChunkSize;
        int offsetY = Position.y * ChunkBuildResult.ChunkSize;

        List<TileChangeData> changes = new ();
        List<TileChangeData> waterTiles = new ();
        
        var unique = data.props.Select(t=>t.propName).Distinct().ToList();

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

                var tileData = GetTile(offsetX +x,offsetY +y,biome, height, moisture, temperature, isCliff, out var color);
                changes.Add(new TileChangeData(tilePosition, tileData, color, Matrix4x4.identity));

                if (isWater && !worldData.heightMapDebug)
                {
                    tileData = WaterTile;
                    float depth = Mathf.InverseLerp(0, worldData.waterHeight, height);
                    waterTiles.Add(new TileChangeData(tilePosition, tileData, new Color(depth,depth,depth), Matrix4x4.identity));
                }
            }
        }
        
        _groundTilemap.SetTiles(changes.ToArray(), true);

        if(worldData.heightMapDebug) return;
        
        _waterTilemap.SetTiles(waterTiles.ToArray(), true);
        
        foreach (var propSpawnData in data.props)
        {
            var rule = worldData.propSpawnRules.First(t=>t.name == propSpawnData.propName);

            props.Add(nodePool.Spawn(propSpawnData.NodeId, propSpawnData, rule.nodeData, propSpawnData.terrainSample));
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
    
    [Inject] private WorldData _worldData;
    
    private void Awake()
    {
        //_groundTilemap = GetComponent<Tilemap>();
        //if(_groundTilemap == null)
        //    throw new Exception("Tilemap not found");
    }

    private TileBase GetTile(int x, int y, BiomeBlend biome, float height, float moisture, float temperature, bool isCliff, out Color color)
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
            item.UnloadProps();
            base.OnDespawned(item);
        }
    }
}
