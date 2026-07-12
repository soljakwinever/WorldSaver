using System;
using System.Collections.Generic;
using System.Linq;
using Project.Scripts;
using Project.Scripts.Bus;
using Project.Scripts.Interface;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using Zenject;

public class Chunkloader : MonoBehaviour, IChunkLoader
{
    [Inject] private IChunkGenerator chunkGenerator;
    
    public Vector2Int Position
    {
        get
        {
            int chunkX = Mathf.FloorToInt(track.position.x / ChunkBuildResult.ChunkSize);
            int chunkY = Mathf.FloorToInt(track.position.y / ChunkBuildResult.ChunkSize);
            return new Vector2Int(chunkX, chunkY);
        }
    }

    [SerializeField] private GameObject cursor;
    
    [Inject] private WorldGeneration worldGeneration;
    [Inject] private MapSignalBus mapSignalBus;
    
    private Vector2Int _lastPosition;
    
    private float tickTimer;
    public const int TickTime = 1;

    public int LoadDistance = 3;
    
    public const int ChunkUnloadTicks = 4; 

    public Transform track;
    
    private Dictionary<Vector2Int, ChunkInstance> _loadedChunks = new Dictionary<Vector2Int, ChunkInstance>();

    [Inject] private Chunk.Pool chunkPool;
    
    private class ChunkInstance
    {
        public IChunk chunk;
        public int ticksSinceLastTouch;

        public void Tick()
        {
            ticksSinceLastTouch++;
        }
        
        public void Touch()
        {
            ticksSinceLastTouch = 0;
        }
    }
        
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        mapSignalBus.ChunkBuilt += MapSignalBusOnChunkBuilt;
        if (!chunkGenerator.IsRunning)
            chunkGenerator.Run(this,destroyCancellationToken);
    }

    private void MapSignalBusOnChunkBuilt(ChunkBuildResult result)
    {
        _loadedChunks.Add(result.chunkPosition, new ChunkInstance {chunk = chunkPool.Spawn(result)}); 
    }

    public void ReloadChunks()
    {
        UnloadChunks(new List<Vector2Int>(_loadedChunks.Keys));
        TouchChunks();
    }

    void TouchChunks()
    {
        int yMin = Position.y - LoadDistance;
        int yMax = Position.y + LoadDistance;
        int xMin = Position.x - LoadDistance;
        int xMax = Position.x + LoadDistance;
        
        List<Vector2Int> requestedChunks = new List<Vector2Int>();
        
        for (int y = yMin; y < yMax; y++)
        {
            for (int x = xMin; x < xMax; x++)
            {
                if (!_loadedChunks.ContainsKey(new Vector2Int(x, y)))
                {
                    requestedChunks.Add(new Vector2Int(x, y));
                }
                else
                {
                    _loadedChunks[new Vector2Int(x, y)].Touch();
                }
            }
        }
        
        foreach (var chunk in requestedChunks.OrderBy(t=> Vector2.Distance(track.position, t )))
            CreateChunk(chunk);
    }
    
    
    public void ReportSpawn(IChunk chunk)
    {

    }

    private void CreateChunk(Vector2Int position)
    {
        chunkGenerator.RequestChunk(position);
    }

    private void OnGUI()
    {
        var labelPosition = new Rect(0, 16, 256, 16);
        GUI.Label(labelPosition, Position.ToString());

        var screenMouse = Mouse.current.position.ReadValue();
        var mousePosition = new Vector3(screenMouse.x, screenMouse.y, -10);
        var worldMouse = Camera.main.ScreenToWorldPoint(mousePosition);

        var grid = FindFirstObjectByType<Grid>();
        var position = grid.WorldToCell(worldMouse);
                
        labelPosition.y += 16;
        GUI.Label(labelPosition, $"Cursor: {position}");
        
        cursor.transform.position = position;
        
        var tile = worldGeneration.GetTerrainSample(position.x, position.y);
        
        labelPosition.y += 16;
        GUI.Label(labelPosition, $"Height: {tile.height}");
        labelPosition.y += 16;
        GUI.Label(labelPosition, $"Moisture: {tile.moisture}");
        labelPosition.y += 16;
        GUI.Label(labelPosition, $"Temperature: {tile.temperature}");
        
        labelPosition.y += 16;
        GUI.Label(labelPosition, $"Biome: {tile.biome.name}");
    }

    // Update is called once per frame
    void Update()
    {
        tickTimer += Time.deltaTime;
        if (tickTimer < TickTime)
        {
            List<Vector2Int> toRemove = new List<Vector2Int>();
            foreach (var chunkInstance in _loadedChunks)
            {
                chunkInstance.Value.Tick();
                if (chunkInstance.Value.ticksSinceLastTouch > ChunkUnloadTicks)
                    toRemove.Add(chunkInstance.Key);
            }
            
            UnloadChunks(toRemove);
            
            tickTimer = 0;
        }
        
        TouchChunks();
    }

    private void UnloadChunks(List<Vector2Int> toRemove)
    {
        foreach (var chunk in toRemove)
        {
            //Todo: Apply changes
            chunkPool.Despawn(_loadedChunks[chunk].chunk as Chunk);
            chunkGenerator.ChunkUnloaded(chunk);
            _loadedChunks.Remove(chunk);
        }
    }
}
