using System;
using System.Collections.Generic;
using System.Linq;
using IngameDebugConsole;
using Project.Scripts;
using Project.Scripts.Bus;
using Project.Scripts.Interface;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Gameplay;
using Project.Scripts.TimeAndWeather;
using UnityEngine;
using UnityEngine.InputSystem;
using Zenject;

public class Chunkloader : MonoBehaviour, IChunkLoader, IWaterTileQuery
{
    [Inject] private IChunkGenerator chunkGenerator;
    [Inject] private PlaneSelection planeSelection;
    
    public Vector2Int Position
    {
        get
        {
            int chunkX = Mathf.FloorToInt(track.position.x / ChunkBuildResult.ChunkSize);
            int chunkY = Mathf.FloorToInt(track.position.y / ChunkBuildResult.ChunkSize);
            return new Vector2Int(chunkX, chunkY);
        }
    }

    public int LoadedChunks => _loadedChunks.Count;
    public Vector2Int WorldSpawnPosition => worldGeneration.WorldSpawnPosition;
    public PlaneData CurrentPlane => planeSelection.Plane;
    public string CurrentPlaneId => planeSelection.PlaneId;
    public float CurrentAmbientTemperature =>
        track == null ? 0f : weatherService.GetAmbientTemperature(track.position);
    public WeatherSample CurrentWeather =>
        track == null ? default : weatherService.Sample(track.position);

    public bool ShouldLoadChunk(Vector2Int chunkPosition)
    {
        if (_portalPinnedChunks.Contains(chunkPosition))
            return true;
        if (track == null)
            return false;

        Vector2Int loaderPosition = Position;
        return chunkPosition.x >= loaderPosition.x - LoadDistance &&
               chunkPosition.x < loaderPosition.x + LoadDistance &&
               chunkPosition.y >= loaderPosition.y - LoadDistance &&
               chunkPosition.y < loaderPosition.y + LoadDistance;
    }

    [SerializeField] private GameObject cursor;
    
    [Inject] private WorldGeneration worldGeneration;
    [Inject] private MapSignalBus mapSignalBus;
    [Inject] private TimeSignalBus timeSignalBus;
    [Inject] private WorldData worldData;
    [Inject] private IRegionalWeatherService weatherService;
    [Inject] private WorldTilemapRenderer worldTilemapRenderer;
    [Inject] private RoomDetectionSystem roomDetectionSystem;
    
    private Vector2Int _lastPosition;
    private bool _hasTouchedPosition;
    private Grid gameGrid;
    
    private float tickTimer;
    public const int TickTime = 1;

    public int LoadDistance = 3;

    [Header("Seasonal Color Refresh")]
    [SerializeField, Min(1)]
    private int biomeColorCellsPerChunkStep = 64;

    [SerializeField, Min(1)]
    private int maxBiomeColorCellsPerFrame = 4096;
    
    public const int ChunkUnloadTicks = 4; 

    public Transform track;
    
    private Dictionary<Vector2Int, ChunkInstance> _loadedChunks = new Dictionary<Vector2Int, ChunkInstance>();
    private readonly Queue<Chunk> _biomeColorRefreshQueue = new();
    private readonly HashSet<Vector2Int> _portalPinnedChunks = new();
    private float _biomeColorRefreshCellsPerSecond;
    private float _biomeColorRefreshCellAccumulator;

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
        gameGrid = FindAnyObjectByType<Grid>();
        mapSignalBus.ChunkBuilt += MapSignalBusOnChunkBuilt;
        timeSignalBus.HourChanged += OnHourChanged;
        timeSignalBus.DayChanged += OnDayChanged;
        timeSignalBus.MonthChanged += OnMonthChanged;
        DebugLogConsole.AddCommand(
            "chunkloader.region",
            "Prints the chunk loader's current region.",
            DebugPrintCurrentRegion);
        DebugLogConsole.AddCommand(
            "chunkloader.reload",
            "Unloads and reloads every currently loaded chunk.",
            ReloadChunks);
        DebugLogConsole.AddCommand<string>(
            "plane.portal",
            "Creates a portal to a plane at the player's current coordinates.",
            DebugCreatePlanePortal,
            "planeId");
        if (!chunkGenerator.IsRunning)
            chunkGenerator.Run(this,destroyCancellationToken);
    }

    private void OnDestroy()
    {
        mapSignalBus.ChunkBuilt -= MapSignalBusOnChunkBuilt;
        timeSignalBus.HourChanged -= OnHourChanged;
        timeSignalBus.DayChanged -= OnDayChanged;
        timeSignalBus.MonthChanged -= OnMonthChanged;
        DebugLogConsole.RemoveCommand(DebugPrintCurrentRegion);
        DebugLogConsole.RemoveCommand(ReloadChunks);
        DebugLogConsole.RemoveCommand<string>(DebugCreatePlanePortal);
    }

    private void DebugPrintCurrentRegion()
    {
        if (track == null)
        {
            Debug.LogWarning(
                "Chunk loader cannot determine its current region because no tracked transform is assigned.");
            return;
        }

        Vector2Int chunkPosition = Position;
        Vector2Int regionPosition =
            WorldPartition.ChunkToRegion(chunkPosition);
        Debug.Log(
            $"Chunk loader region: {regionPosition} (chunk: {chunkPosition}).");
    }

    private void DebugCreatePlanePortal(string planeId)
    {
        if (track == null)
        {
            Debug.LogWarning("Cannot create a plane portal without a tracked player.");
            return;
        }
        if (!worldData.TryGetPlane(planeId, out PlaneData destination))
        {
            string available = string.Join(", ",
                (worldData.planes ?? Array.Empty<PlaneData>())
                .Where(candidate => candidate != null)
                .Select(candidate => candidate.PersistentId));
            Debug.LogWarning(
                $"Unknown plane '{planeId}'. Available planes: {available}.");
            return;
        }
        if (string.Equals(destination.PersistentId, CurrentPlaneId,
                StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogWarning($"Player is already in plane '{CurrentPlaneId}'.");
            return;
        }

        GameObject prefab = Resources.Load<GameObject>("Portal");
        if (prefab == null)
        {
            Debug.LogWarning("Could not load Resources/Portal.");
            return;
        }

        WorldGeneration destinationGeneration = new(
            worldData,
            new WorldGenerationSelection(
                unchecked((int)worldGeneration.Seed),
                destination.generationPreset),
            null);
        Vector2Int requestedCell = Vector2Int.FloorToInt(track.position);
        if (!destinationGeneration.TryFindSafePortalPosition(
                requestedCell,
                out Vector2Int safeCell))
        {
            Debug.LogWarning(
                $"No safe portal landing area was found within 64 cells of " +
                $"{requestedCell} in plane '{destination.PersistentId}'.");
            return;
        }

        Vector3 destinationPosition = safeCell == requestedCell
            ? track.position
            : new Vector3(safeCell.x + 0.5f, safeCell.y + 0.5f,
                track.position.z);
        GameObject instance = Instantiate(
            prefab,
            track.position + Vector3.right * 2f,
            Quaternion.identity);
        EventPortalEffect effect = new()
        {
            destinationWorldPosition = destinationPosition,
            activationDistance = 1.5f,
            transitionDuration = 0.45f,
            creationDuration = 0.5f,
            textureSize = 256,
            particleSizeMultiplier = 6f
        };
        FastTravelPortal portal =
            instance.GetComponent<FastTravelPortal>() ??
            instance.AddComponent<FastTravelPortal>();
        portal.Initialize(effect, track, this, destination);
        Debug.Log(
            $"Created portal from plane '{CurrentPlaneId}' to " +
            $"'{destination.PersistentId}' at {destinationPosition}" +
            (safeCell == requestedCell
                ? "."
                : $" (nearest safe area to {requestedCell})."));
    }

    private void OnHourChanged(TimeChangedArgs args)
    {
        if (args.Hour < SeasonalBiomeTint.HoursInDay)
            RefreshLoadedChunkColors(args.Hour);
    }

    private void OnDayChanged(TimeChangedArgs _)
    {
        RefreshLoadedChunkColors(0);
    }

    private void OnMonthChanged(TimeChangedArgs _)
    {
        // DayChanged is raised before TimeController advances an overflowing
        // day into its new season, so refresh once more with the final season.
        RefreshLoadedChunkColors(0);
    }

    private void RefreshLoadedChunkColors(int hour)
    {
        _biomeColorRefreshQueue.Clear();
        int pendingCellCount = 0;
        foreach (ChunkInstance instance in _loadedChunks.Values)
        {
            if (instance.chunk is Chunk chunk)
            {
                chunk.BeginBiomeColorRefresh(hour);
                if (chunk.RemainingBiomeColorRefreshCells > 0)
                {
                    _biomeColorRefreshQueue.Enqueue(chunk);
                    pendingCellCount += chunk.RemainingBiomeColorRefreshCells;
                }
            }
        }

        _biomeColorRefreshCellsPerSecond =
            pendingCellCount / Mathf.Max(
                0.1f,
                worldData.minutesPerDay * 60f /
                SeasonalBiomeTint.HoursInDay);
        _biomeColorRefreshCellAccumulator = 0f;
    }

    private void ProcessBiomeColorRefresh()
    {
        if (_biomeColorRefreshQueue.Count == 0)
            return;

        _biomeColorRefreshCellAccumulator +=
            _biomeColorRefreshCellsPerSecond * Time.deltaTime;
        int remainingBudget = Mathf.Min(
            Mathf.FloorToInt(_biomeColorRefreshCellAccumulator),
            maxBiomeColorCellsPerFrame);
        _biomeColorRefreshCellAccumulator -= remainingBudget;

        while (remainingBudget > 0 && _biomeColorRefreshQueue.Count > 0)
        {
            Chunk chunk = _biomeColorRefreshQueue.Dequeue();
            if (!IsLoaded(chunk))
                continue;

            int applied = Mathf.Min(
                remainingBudget,
                biomeColorCellsPerChunkStep,
                chunk.RemainingBiomeColorRefreshCells);
            bool completed = chunk.RefreshBiomeColorCells(applied);
            remainingBudget -= applied;

            if (!completed)
                _biomeColorRefreshQueue.Enqueue(chunk);
        }
    }

    private bool IsLoaded(Chunk chunk)
    {
        foreach (ChunkInstance instance in _loadedChunks.Values)
        {
            if (ReferenceEquals(instance.chunk, chunk))
                return true;
        }

        return false;
    }

    private void MapSignalBusOnChunkBuilt(ChunkBuildResult result)
    {
        Chunk chunk = chunkPool.Spawn(result);
        _loadedChunks.Add(result.chunkPosition, new ChunkInstance {chunk = chunk});
        mapSignalBus.RaiseChunkLoaded(result.chunkPosition, chunk);
    }

    public void ReloadChunks()
    {
        UnloadChunks(new List<Vector2Int>(_loadedChunks.Keys));
        TouchChunks();
    }

    void TouchChunks()
    {
        Vector2Int loaderPosition = Position;
        _lastPosition = loaderPosition;
        _hasTouchedPosition = true;

        int yMin = loaderPosition.y - LoadDistance;
        int yMax = loaderPosition.y + LoadDistance;
        int xMin = loaderPosition.x - LoadDistance;
        int xMax = loaderPosition.x + LoadDistance;
        
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

        foreach (Vector2Int pinned in _portalPinnedChunks)
        {
            if (_loadedChunks.TryGetValue(pinned, out ChunkInstance loaded))
                loaded.Touch();
            else if (!requestedChunks.Contains(pinned))
                requestedChunks.Add(pinned);
        }
        
        Vector2 trackedPosition = track.position;
        float chunkHalfSize = ChunkBuildResult.ChunkSize * 0.5f;

        foreach (var chunk in requestedChunks.OrderBy(chunkPosition =>
                 {
                     Vector2 chunkCenter = new Vector2(
                         chunkPosition.x * ChunkBuildResult.ChunkSize + chunkHalfSize,
                         chunkPosition.y * ChunkBuildResult.ChunkSize + chunkHalfSize);
                     return (chunkCenter - trackedPosition).sqrMagnitude;
                 }))
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

    public bool IsWaterTile(Vector3Int worldCell)
    {
        return TryGetLoadedChunk(worldCell, out Chunk chunk) &&
               chunk.TryGetTileData(
                   worldCell,
                   PersistentTileLayer.Water,
                   out TileData water) &&
               water != null;
    }

    public void CopyLoadedChunks(List<Chunk> destination)
    {
        if (destination == null)
            throw new ArgumentNullException(nameof(destination));

        destination.Clear();
        foreach (ChunkInstance instance in _loadedChunks.Values)
        {
            if (instance.chunk is Chunk chunk)
                destination.Add(chunk);
        }
    }

    private void CreateChunk(Vector2Int position)
    {
        chunkGenerator.RequestChunk(position);
    }

    // private void OnGUI()
    // {
    //     var labelPosition = new Rect(0, 16, 256, 16);
    //     GUI.Label(labelPosition, Position.ToString());
    //
    //     var screenMouse = Mouse.current.position.ReadValue();
    //     var mousePosition = new Vector3(screenMouse.x, screenMouse.y, -10);
    //     var worldMouse = Camera.main.ScreenToWorldPoint(mousePosition);
    //
    //     var position = gameGrid.WorldToCell(worldMouse);
    //             
    //     labelPosition.y += 16;
    //     GUI.Label(labelPosition, $"Cursor: {position}");
    //     
    //     cursor.transform.position = position;
    //     
    //     var tile = worldGeneration.GetTerrainSample(position.x, position.y);
    //     
    //     labelPosition.y += 16;
    //     GUI.Label(labelPosition, $"Height: {tile.height}");
    //     labelPosition.y += 16;
    //     GUI.Label(labelPosition, $"Moisture: {tile.moisture}");
    //     labelPosition.y += 16;
    //     GUI.Label(labelPosition, $"Temperature: {tile.temperature}");
    //     
    //     labelPosition.y += 16;
    //     GUI.Label(labelPosition, $"Biome: {tile.biome.name}");
    // }

    // Update is called once per frame
    void Update()
    {
        ProcessBiomeColorRefresh();

        tickTimer += Time.deltaTime;
        bool maintenanceTick = tickTimer >= TickTime;
        bool loaderMoved = !_hasTouchedPosition || Position != _lastPosition;

        if (loaderMoved || maintenanceTick)
            TouchChunks();

        if (maintenanceTick)
        {
            List<Vector2Int> toRemove = new List<Vector2Int>();
            foreach (var chunkInstance in _loadedChunks)
            {
                chunkInstance.Value.Tick();
                if (chunkInstance.Value.ticksSinceLastTouch > ChunkUnloadTicks)
                    toRemove.Add(chunkInstance.Key);
            }
            
            UnloadChunks(toRemove);
            
            tickTimer -= TickTime;
        }

    }


    /// <summary> Keeps only the four chunks intersecting a portal preview alive. </summary>
    public void SetPortalPreview(Vector2 worldPosition, bool enabled)
    {
        _portalPinnedChunks.Clear();
        if (!enabled)
            return;

        float size = ChunkBuildResult.ChunkSize;
        int x = Mathf.FloorToInt(worldPosition.x / size);
        int y = Mathf.FloorToInt(worldPosition.y / size);
        // A preview camera can straddle both axes, but never needs more than 2x2.
        _portalPinnedChunks.Add(new Vector2Int(x, y));
        _portalPinnedChunks.Add(new Vector2Int(x - 1, y));
        _portalPinnedChunks.Add(new Vector2Int(x, y - 1));
        _portalPinnedChunks.Add(new Vector2Int(x - 1, y - 1));
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
            if (instance.chunk is Chunk roomChunk)
                roomDetectionSystem?.NotifyChunkUnloading(roomChunk);
            worldTilemapRenderer.RemoveChunk(chunkPosition);
            mapSignalBus.RaiseChunkUnloaded(chunkPosition);

            if (instance.chunk is Chunk chunk)
                chunkPool.Despawn(chunk);
            else
                Debug.LogError(
                    $"Loaded chunk {chunkPosition} is not a {nameof(Chunk)} and cannot be returned to its pool.");
        }
    }
}
