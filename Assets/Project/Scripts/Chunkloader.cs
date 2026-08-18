using System;
using System.Collections.Generic;
using System.Linq;
using IngameDebugConsole;
using Project.Scripts;
using Project.Scripts.Bus;
using Project.Scripts.Core;
using Project.Scripts.Interface;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Gameplay;
using Project.Scripts.TimeAndWeather;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Profiling;
using Zenject;

public class Chunkloader : MonoBehaviour, IChunkLoader, IWaterTileQuery
{
    [Inject] private IChunkGenerator chunkGenerator;
    [Inject] private PlaneSelection planeSelection;
    
    public Vector2Int Position
    {
        get
        {
            Vector2 focus = _hasStreamingFocus
                ? _streamingFocus
                : (Vector2)track.position;
            int chunkX = Mathf.FloorToInt(focus.x / ChunkBuildResult.ChunkSize);
            int chunkY = Mathf.FloorToInt(focus.y / ChunkBuildResult.ChunkSize);
            return new Vector2Int(chunkX, chunkY);
        }
    }

    public int LoadedChunks => _loadedChunks.Count;
    public int PendingChunkInitializations => _pendingInitializations.Count;
    public Vector2Int WorldSpawnPosition => worldGeneration.WorldSpawnPosition;
    public PlaneData CurrentPlane => planeSelection.Plane;
    public string CurrentPlaneId => planeSelection.PlaneId;
    public float CurrentAmbientTemperature =>
        track == null ? 0f : weatherService.GetAmbientTemperature(track.position);
    public WeatherSample CurrentWeather =>
        track == null ? default : weatherService.Sample(track.position);

    public bool ShouldLoadChunk(Vector2Int chunkPosition)
    {
        if (_portalPinnedChunks.Contains(chunkPosition) ||
            _transitPinnedChunks.Contains(chunkPosition))
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
    [Inject] private WorldClock worldClock;
    [Inject] private IRegionalWeatherService weatherService;
    [Inject] private WorldTilemapRenderer worldTilemapRenderer;
    [Inject] private RoomDetectionSystem roomDetectionSystem;
    [Inject] private IRegionRepository regionRepository;
    
    private Vector2Int _lastPosition;
    private bool _hasTouchedPosition;
    private Grid gameGrid;
    
    private float tickTimer;
    public const int TickTime = 1;

    public Material coverageMaterial;

    public int LoadDistance = 3;

    [Header("Chunk Initialization")]
    [SerializeField, Min(0.1f)]
    private float chunkInitializationBudgetMilliseconds = 2f;

    [SerializeField, Min(1)]
    private int maxChunkLoadNotificationsPerFrame = 1;

    [SerializeField, Min(1)]
    private int maxPlaneTransitionUnloadsPerFrame = 2;

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
    private readonly HashSet<Vector2Int> _transitPinnedChunks = new();
    private readonly List<PendingChunkInitialization> _pendingInitializations = new();
    private readonly HashSet<Vector2Int> _pendingInitializationPositions = new();
    private readonly List<Vector2Int> _requestedChunks = new();
    private readonly HashSet<Vector2Int> _requestedChunkSet = new();
    private readonly List<Vector2Int> _chunksToRemove = new();
    private readonly Queue<PendingChunkInitialization> _completedInitializations = new();
    private bool _pendingOrderDirty;
    private bool _hasStreamingFocus;
    private Vector2 _streamingFocus;
    private bool _planeTransitionInProgress;
    private bool _planeTransitionDraining;

    private static readonly ProfilerMarker InitializationMarker =
        new("Chunkloader.Initialization");
    private static readonly ProfilerMarker NotificationMarker =
        new("Chunkloader.LoadNotifications");
    private static readonly ProfilerMarker MaintenanceMarker =
        new("Chunkloader.Maintenance");
    private static readonly ProfilerMarker UnloadMarker =
        new("Chunkloader.Unload");
    private float _biomeColorRefreshCellsPerSecond;
    private float _biomeColorRefreshCellAccumulator;
    private CoverageAreaRenderer _coverageRenderer;
    private CoverageParticlePresenter _coverageParticlePresenter;

    private sealed class PendingChunkInitialization
    {
        public readonly Chunk Chunk;
        public readonly Vector2Int Position;

        public PendingChunkInitialization(Chunk chunk, Vector2Int position)
        {
            Chunk = chunk;
            Position = position;
        }
    }

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
        Camera coverageCamera = track != null
            ? track.GetComponentInChildren<Camera>(includeInactive: true)
            : null;
        coverageCamera ??= Camera.main;
        _coverageRenderer = new CoverageAreaRenderer(
            transform,
            worldData,
            worldClock,
            coverageCamera,
            Position,
            LoadDistance,
            coverageMaterial);
        _coverageParticlePresenter = new CoverageParticlePresenter(
            track, worldData, _coverageRenderer);
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
        _coverageParticlePresenter?.Dispose();
        _coverageParticlePresenter = null;
        _coverageRenderer?.Dispose();
        _coverageRenderer = null;
        mapSignalBus.ChunkBuilt -= MapSignalBusOnChunkBuilt;
        timeSignalBus.HourChanged -= OnHourChanged;
        timeSignalBus.DayChanged -= OnDayChanged;
        timeSignalBus.MonthChanged -= OnMonthChanged;
        DebugLogConsole.RemoveCommand(DebugPrintCurrentRegion);
        DebugLogConsole.RemoveCommand(ReloadChunks);
        DebugLogConsole.RemoveCommand<string>(DebugCreatePlanePortal);
        while (_pendingInitializations.Count > 0)
            CancelPendingInitialization(_pendingInitializations.Count - 1);
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
        return chunk != null &&
               _loadedChunks.TryGetValue(chunk.Position, out ChunkInstance instance) &&
               ReferenceEquals(instance.chunk, chunk);
    }

    private void MapSignalBusOnChunkBuilt(ChunkBuildResult result)
    {
        if (_loadedChunks.ContainsKey(result.chunkPosition) ||
            !_pendingInitializationPositions.Add(result.chunkPosition))
        {
            return;
        }

        Chunk chunk = chunkPool.Spawn(result);
        _pendingInitializations.Add(
            new PendingChunkInitialization(chunk, result.chunkPosition));
        _pendingOrderDirty = true;
    }

    private void ProcessChunkInitializations()
    {
        if (_pendingInitializations.Count == 0)
            return;

        using ProfilerMarker.AutoScope initializationScope =
            InitializationMarker.Auto();
        if (_pendingOrderDirty)
        {
            Vector2 trackedPosition = _hasStreamingFocus
                ? _streamingFocus
                : track != null ? track.position : Vector2.zero;
            float size = ChunkBuildResult.ChunkSize;
            float halfSize = size * 0.5f;
            _pendingInitializations.Sort((left, right) =>
            {
                float leftX = left.Position.x * size + halfSize - trackedPosition.x;
                float leftY = left.Position.y * size + halfSize - trackedPosition.y;
                float rightX = right.Position.x * size + halfSize - trackedPosition.x;
                float rightY = right.Position.y * size + halfSize - trackedPosition.y;
                int distance = (leftX * leftX + leftY * leftY).CompareTo(
                    rightX * rightX + rightY * rightY);
                if (distance != 0)
                    return distance;
                int x = left.Position.x.CompareTo(right.Position.x);
                return x != 0 ? x : left.Position.y.CompareTo(right.Position.y);
            });
            _pendingOrderDirty = false;
        }

        double deadline = Time.realtimeSinceStartupAsDouble +
                          chunkInitializationBudgetMilliseconds / 1000d;
        int index = 0;
        do
        {
            if (index >= _pendingInitializations.Count)
                index = 0;

            PendingChunkInitialization pending = _pendingInitializations[index];
            if (!ShouldLoadChunk(pending.Position))
            {
                CancelPendingInitialization(index);
                continue;
            }

            ChunkInitializationStatus status = pending.Chunk.AdvanceInit(deadline);
            if (status == ChunkInitializationStatus.InProgress)
            {
                index++;
                if (pending.Chunk.LastInitializationStepWasExpensive)
                    break;
                continue;
            }

            _pendingInitializations.RemoveAt(index);
            _pendingInitializationPositions.Remove(pending.Position);
            if (status == ChunkInitializationStatus.Failed)
            {
                worldTilemapRenderer.RemoveChunk(pending.Position);
                chunkPool.Despawn(pending.Chunk);
                chunkGenerator.ChunkUnloaded(pending.Position);
                continue;
            }

            _loadedChunks.Add(
                pending.Position,
                new ChunkInstance { chunk = pending.Chunk });
            _coverageRenderer?.SetActive(pending.Position, true);
            _completedInitializations.Enqueue(pending);
            if (pending.Chunk.LastInitializationStepWasExpensive)
                break;
        } while (_pendingInitializations.Count > 0 &&
                 Time.realtimeSinceStartupAsDouble < deadline);
    }

    private void CancelPendingInitialization(int index)
    {
        PendingChunkInitialization pending = _pendingInitializations[index];
        _pendingInitializations.RemoveAt(index);
        _pendingInitializationPositions.Remove(pending.Position);
        worldTilemapRenderer.RemoveChunk(pending.Position);
        chunkPool.Despawn(pending.Chunk);
        chunkGenerator.ChunkUnloaded(pending.Position);
    }

    public void ReloadChunks()
    {
        while (_pendingInitializations.Count > 0)
            CancelPendingInitialization(_pendingInitializations.Count - 1);
        _chunksToRemove.Clear();
        _chunksToRemove.AddRange(_loadedChunks.Keys);
        UnloadChunks(_chunksToRemove);
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
        
        _requestedChunks.Clear();
        _requestedChunkSet.Clear();
        
        for (int y = yMin; y < yMax; y++)
        {
            for (int x = xMin; x < xMax; x++)
            {
                Vector2Int position = new(x, y);
                if (!_loadedChunks.TryGetValue(position, out ChunkInstance loaded))
                {
                    if (_requestedChunkSet.Add(position))
                        _requestedChunks.Add(position);
                }
                else
                {
                    loaded.Touch();
                }
            }
        }

        foreach (Vector2Int pinned in _portalPinnedChunks)
        {
            if (_loadedChunks.TryGetValue(pinned, out ChunkInstance loaded))
                loaded.Touch();
            else if (_requestedChunkSet.Add(pinned))
                _requestedChunks.Add(pinned);
        }

        foreach (Vector2Int pinned in _transitPinnedChunks)
        {
            if (_loadedChunks.TryGetValue(pinned, out ChunkInstance loaded))
                loaded.Touch();
            else if (_requestedChunkSet.Add(pinned))
                _requestedChunks.Add(pinned);
        }
        
        Vector2 trackedPosition = _hasStreamingFocus
            ? _streamingFocus
            : (Vector2)track.position;
        float chunkHalfSize = ChunkBuildResult.ChunkSize * 0.5f;

        _requestedChunks.Sort((left, right) =>
        {
            Vector2 leftCenter = (Vector2)left * ChunkBuildResult.ChunkSize +
                                 Vector2.one * chunkHalfSize;
            Vector2 rightCenter = (Vector2)right * ChunkBuildResult.ChunkSize +
                                  Vector2.one * chunkHalfSize;
            int distance = (leftCenter - trackedPosition).sqrMagnitude.CompareTo(
                (rightCenter - trackedPosition).sqrMagnitude);
            if (distance != 0)
                return distance;
            int x = left.x.CompareTo(right.x);
            return x != 0 ? x : left.y.CompareTo(right.y);
        });
        foreach (Vector2Int chunk in _requestedChunks)
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

    internal bool TryCopyVisibleLoadedChunks(List<Chunk> destination)
    {
        if (destination == null)
            throw new ArgumentNullException(nameof(destination));

        destination.Clear();
        if (_coverageRenderer == null)
            return false;

        _coverageRenderer.GetVisibleChunkBoundsClamped(
            out Vector2Int minimum,
            out Vector2Int maximumExclusive);
        if (maximumExclusive.x <= minimum.x ||
            maximumExclusive.y <= minimum.y)
            return false;

        for (int y = minimum.y; y < maximumExclusive.y; y++)
        {
            for (int x = minimum.x; x < maximumExclusive.x; x++)
            {
                Vector2Int position = new(x, y);
                if (!_loadedChunks.TryGetValue(
                        position, out ChunkInstance instance) ||
                    instance.chunk is not Chunk chunk)
                {
                    destination.Clear();
                    return false;
                }
                destination.Add(chunk);
            }
        }
        return destination.Count > 0;
    }

    internal void SubmitCoverage(
        Vector2Int position,
        Color32[] pixels,
        CoverageData[] slots,
        bool transition = false) =>
        _coverageRenderer?.Submit(position, pixels, slots, transition);

    internal void ClearCoverage(Vector2Int position) =>
        _coverageRenderer?.Remove(position);

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
        _coverageRenderer?.SetFootprint(Position, LoadDistance);
        if (_planeTransitionDraining)
        {
            _coverageRenderer?.Tick();
            _coverageParticlePresenter?.Tick();
            return;
        }

        ProcessChunkLoadNotifications();
        ProcessChunkInitializations();
        ProcessBiomeColorRefresh();
        _coverageRenderer?.Tick();
        _coverageParticlePresenter?.Tick();

        tickTimer += Time.deltaTime;
        bool maintenanceTick = tickTimer >= TickTime;
        bool loaderMoved = !_hasTouchedPosition || Position != _lastPosition;
        if (loaderMoved || maintenanceTick)
        {
            if (loaderMoved)
                _pendingOrderDirty = true;
            TouchChunks();
        }

        if (maintenanceTick)
        {
            using (MaintenanceMarker.Auto())
            {
                _chunksToRemove.Clear();
                foreach (var chunkInstance in _loadedChunks)
                {
                    chunkInstance.Value.Tick();
                    if (chunkInstance.Value.ticksSinceLastTouch > ChunkUnloadTicks)
                        _chunksToRemove.Add(chunkInstance.Key);
                }

                UnloadChunks(_chunksToRemove);
            }
            
            tickTimer -= TickTime;
        }

    }

    private void ProcessChunkLoadNotifications()
    {
        using ProfilerMarker.AutoScope scope = NotificationMarker.Auto();
        int budget = Mathf.Max(1, maxChunkLoadNotificationsPerFrame);
        while (budget-- > 0 && _completedInitializations.Count > 0)
        {
            PendingChunkInitialization completed =
                _completedInitializations.Dequeue();
            if (_loadedChunks.TryGetValue(
                    completed.Position,
                    out ChunkInstance loaded) &&
                ReferenceEquals(loaded.chunk, completed.Chunk))
            {
                mapSignalBus.RaiseChunkLoaded(
                    completed.Position,
                    completed.Chunk);
            }
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

    /// <summary>Keeps an authored elevator/lobby chunk alive during plane travel.</summary>
    public void SetTransitSpacePinned(Vector2 worldPosition, bool enabled)
    {
        _transitPinnedChunks.Clear();
        if (enabled)
        {
            float size = ChunkBuildResult.ChunkSize;
            _transitPinnedChunks.Add(new Vector2Int(
                Mathf.FloorToInt(worldPosition.x / size),
                Mathf.FloorToInt(worldPosition.y / size)));
        }

        TouchChunks();
    }

    public async Awaitable<bool> PreparePlaneTransitionAsync(
        PlaneData destination,
        Vector3 landingPosition,
        System.Threading.CancellationToken cancellationToken)
    {
        if (_planeTransitionInProgress || destination == null ||
            destination.generationPreset == null)
        {
            return false;
        }

        _planeTransitionInProgress = true;
        _planeTransitionDraining = true;
        try
        {
            await chunkGenerator.SuspendAndClearAsync(cancellationToken);

            while (_pendingInitializations.Count > 0)
                CancelPendingInitialization(_pendingInitializations.Count - 1);
            _completedInitializations.Clear();
            _biomeColorRefreshQueue.Clear();
            _portalPinnedChunks.Clear();
            _transitPinnedChunks.Clear();
            _requestedChunks.Clear();
            _requestedChunkSet.Clear();

            int unloadBudget = Mathf.Max(
                1,
                maxPlaneTransitionUnloadsPerFrame);
            while (_loadedChunks.Count > 0)
            {
                // This snapshot must remain local. Update and reload paths use
                // _chunksToRemove as scratch storage and can run after yields.
                List<Vector2Int> batch = new(unloadBudget);
                foreach (Vector2Int position in _loadedChunks.Keys)
                {
                    batch.Add(position);
                    if (batch.Count >= unloadBudget)
                        break;
                }

                UnloadChunks(batch);
                if (_loadedChunks.Count > 0)
                    await Awaitable.NextFrameAsync(cancellationToken);
            }

            worldTilemapRenderer.ClearForPlaneTransition();

            if (!IsPlaneDrainComplete())
            {
                string message =
                    "Plane transition attempted to switch generation before " +
                    "all source chunk state was drained. " +
                    $"Loaded={_loadedChunks.Count}, " +
                    $"PendingInit={_pendingInitializations.Count}, " +
                    $"CompletedInit={_completedInitializations.Count}, " +
                    $"GeneratorWork={chunkGenerator.PendingWorkCount}, " +
                    $"Rendered={worldTilemapRenderer.RegisteredChunkCount}.";
                Debug.LogError(message, this);
                throw new InvalidOperationException(message);
            }

            await regionRepository.FlushDirtyAsync();
            await regionRepository.ClearLoadedRegionsAsync();

            worldGeneration.Reconfigure(new WorldGenerationSelection(
                unchecked((int)worldGeneration.Seed),
                destination.generationPreset));
            planeSelection.SetPlane(destination);

            _streamingFocus = landingPosition;
            _hasStreamingFocus = true;
            _hasTouchedPosition = false;
            _pendingOrderDirty = true;
            _planeTransitionDraining = false;
            chunkGenerator.Resume();
            TouchChunks();

            while (!IsDestinationFootprintReady())
                await Awaitable.NextFrameAsync(cancellationToken);

            return true;
        }
        catch
        {
            chunkGenerator.Resume();
            _planeTransitionDraining = false;
            _hasStreamingFocus = false;
            _planeTransitionInProgress = false;
            throw;
        }
    }

    public void CompletePlaneTransition()
    {
        _hasStreamingFocus = false;
        _hasTouchedPosition = false;
        _pendingOrderDirty = true;
        _planeTransitionInProgress = false;
        TouchChunks();
    }

    private bool IsDestinationFootprintReady()
    {
        Vector2Int center = Position;
        for (int y = center.y - LoadDistance; y < center.y + LoadDistance; y++)
        {
            for (int x = center.x - LoadDistance; x < center.x + LoadDistance; x++)
            {
                if (!_loadedChunks.ContainsKey(new Vector2Int(x, y)))
                    return false;
            }
        }
        return true;
    }

    internal bool IsPlaneDrainComplete() =>
        _loadedChunks.Count == 0 &&
        _pendingInitializations.Count == 0 &&
        _pendingInitializationPositions.Count == 0 &&
        _completedInitializations.Count == 0 &&
        chunkGenerator.PendingWorkCount == 0 &&
        worldTilemapRenderer.RegisteredChunkCount == 0;

    private void UnloadChunks(IEnumerable<Vector2Int> toRemove)
    {
        using ProfilerMarker.AutoScope scope = UnloadMarker.Auto();
        foreach (var chunkPosition in toRemove)
        {
            if (!_loadedChunks.TryGetValue(chunkPosition, out ChunkInstance instance))
                continue;

            // Remove ownership before invoking either callback. Despawn performs
            // persistence work which can synchronously trigger another reload;
            // leaving the entry visible until afterwards allowed that path to
            // return the same pooled Chunk a second time.
            _loadedChunks.Remove(chunkPosition);
            _coverageRenderer?.Remove(chunkPosition);
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
