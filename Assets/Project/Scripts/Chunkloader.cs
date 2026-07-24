using System;
using System.Collections.Generic;
using System.Linq;
using Project.Scripts;
using Project.Scripts.Bus;
using Project.Scripts.Interface;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;
using UnityEngine.InputSystem;
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
    [SerializeField] private EntityArchetype mouseSpawnArchetype;
    [SerializeField] private TileData[] mousePlacementTiles;
    
    [Inject] private WorldGeneration worldGeneration;
    [Inject] private WorldData worldData;
    [Inject] private MapSignalBus mapSignalBus;
    
    private Vector2Int _lastPosition;
    
    private float tickTimer;
    public const int TickTime = 1;

    public int LoadDistance = 3;
    
    public const int ChunkUnloadTicks = 4; 

    public Transform track;
    
    private Dictionary<Vector2Int, ChunkInstance> _loadedChunks = new Dictionary<Vector2Int, ChunkInstance>();

    [Inject] private Chunk.Pool chunkPool;

    private int selectedTile = 0;
    
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
        if (mouseSpawnArchetype == null &&
            worldData.runtimeEntityArchetypes?.Length > 0)
        {
            mouseSpawnArchetype = worldData.runtimeEntityArchetypes[0];
        }

        if (mouseSpawnArchetype != null &&
            (worldData.runtimeEntityArchetypes == null ||
             !worldData.runtimeEntityArchetypes.Contains(mouseSpawnArchetype)))
        {
            worldData.runtimeEntityArchetypes =
                (worldData.runtimeEntityArchetypes ?? Array.Empty<EntityArchetype>())
                .Append(mouseSpawnArchetype)
                .ToArray();
        }

        mapSignalBus.ChunkBuilt += MapSignalBusOnChunkBuilt;
        if (!chunkGenerator.IsRunning)
            chunkGenerator.Run(this,destroyCancellationToken);
    }

    private void OnDestroy()
    {
        mapSignalBus.ChunkBuilt -= MapSignalBusOnChunkBuilt;
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

    public bool TryGetLoadedChunk(Vector3Int worldCell, out Chunk chunk)
    {
        Vector2Int chunkPosition = new(
            Mathf.FloorToInt((float)worldCell.x / ChunkBuildResult.ChunkSize),
            Mathf.FloorToInt((float)worldCell.y / ChunkBuildResult.ChunkSize));

        if (_loadedChunks.TryGetValue(chunkPosition, out ChunkInstance loaded) &&
            loaded.chunk is Chunk concreteChunk)
        {
            chunk = concreteChunk;
            return true;
        }

        chunk = null;
        return false;
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

        labelPosition.y += 32;
        GUI.Label(labelPosition,$"Place Tile: {mousePlacementTiles[selectedTile].name}");
    }

    // Update is called once per frame
    void Update()
    {
        if (Mouse.current.scroll.ReadValue().y > 0.01f)
        {
            selectedTile = (selectedTile + 1) % mousePlacementTiles.Length;
        }
        
        if (Mouse.current != null && Camera.main != null &&
            (Mouse.current.leftButton.wasPressedThisFrame ||
             Mouse.current.rightButton.wasPressedThisFrame))
        {
            Vector2 worldPosition =
                Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            Vector2Int chunkPosition = new(
                Mathf.FloorToInt(worldPosition.x / ChunkBuildResult.ChunkSize),
                Mathf.FloorToInt(worldPosition.y / ChunkBuildResult.ChunkSize));
            
            if (_loadedChunks.TryGetValue(chunkPosition, out ChunkInstance loaded) &&
                loaded.chunk is Chunk chunk)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame)
                {
                    Vector3Int worldCell = new(
                        Mathf.FloorToInt(worldPosition.x),
                        Mathf.FloorToInt(worldPosition.y));
                    chunk.TryPlaceTile(
                        worldCell,
                        PersistentTileLayer.Ground,
                        mousePlacementTiles[selectedTile] != null ? mousePlacementTiles[selectedTile] : chunk.WallTile);
                }
                else if (mouseSpawnArchetype != null)
                {
                    chunk.SpawnRuntimeEntity(mouseSpawnArchetype, worldPosition);
                }
            }
        }

        tickTimer += Time.deltaTime;
        if (tickTimer >= TickTime)
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

    private void UnloadChunks(IEnumerable<Vector2Int> toRemove)
    {
        foreach (var chunkPosition in toRemove)
        {
            if (!_loadedChunks.TryGetValue(chunkPosition, out ChunkInstance instance))
                continue;

            // Remove ownership before invoking either callback. Despawn performs
            // persistence work which can synchronously trigger another reload;
            // leaving the entry visible until afterwards allowed that path to
            // return the same pooled Chunk a second time.
            _loadedChunks.Remove(chunkPosition);
            chunkGenerator.ChunkUnloaded(chunkPosition);

            if (instance.chunk is Chunk chunk)
                chunkPool.Despawn(chunk);
            else
                Debug.LogError(
                    $"Loaded chunk {chunkPosition} is not a {nameof(Chunk)} and cannot be returned to its pool.");
        }
    }
}
