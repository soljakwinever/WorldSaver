using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.Entities;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CircleCollider2D))]
    public sealed class VillagerEntityBridge : MonoBehaviour,
        IEntityComponent, IPersistentComponent, IEntityRemovalHandler,
        IDamageable, IEntityDamageSource, IEnemyTarget, IInventory,
        IEquipmentController, IHasNeeds
    {
        public const ushort TypeId = 0x564C; // VL
        private const ushort Version = 2;
        private const float CombatRange = 1.5f;

        [SerializeField] private string villagerName = "Villager";
        [SerializeField] private VillagerRole role = VillagerRole.Generalist;
        [SerializeField] private VillagerJobMask allowedJobs = VillagerJobMask.All;
        [SerializeField, Min(1)] private int maximumHealth = 100;
        [SerializeField, Min(0f)] private float movementSpeed = 3f;
        [SerializeField, Min(0f)] private float workRate = 1f;
        [SerializeField, Min(0f)] private float hungerDrainPerSecond = 0.2f;
        [SerializeField, Min(1)] private int inventorySize = 6;
        [SerializeField, Min(1)] private int maximumPathVisitedTiles = 4096;
        [SerializeField] private string assignedTownId = string.Empty;

        private Entity _entity = Entity.Null;
        private VillagerStats _savedStats;
        private VillagerNeeds _savedNeeds;
        private VillagerAssignment _savedAssignment;
        private readonly List<ItemStack> _savedInventory = new();
        private readonly List<EquippedRecord> _savedEquipment = new();
        private readonly List<IItemStack> _inventoryView = new();
        private readonly List<IItemStack> _equipmentView = new();
        private bool _hasSavedState;
        private bool _savedPendingDelivery;
        private bool _deathHandled;
        private float _nextTownResolutionTime;
        private TownCore _town;
        private ItemCatalog _catalog;
        private IAttackService _attackService;
        private IItemStackExplosionService _dropService;
        private IVillagerWorldAdapter _worldAdapter;
        private IPathFindingService _pathFinder;
        private IPathFindingMap _pathMap;
        private Task<WorkPathResult> _pendingPath;
        private CancellationTokenSource _pathCancellation;
        private long _pendingPathJobId;
        private BedComponent _sleepBed;
        private bool _nightRestActive;
        private bool _sleepingIndoors;
        private bool _enemyRegistered;
        private float _nextSleepAttempt;
        [InjectOptional] private IIndoorLocationService _shelterLocator;
        [InjectOptional] private IAutomaticDoorTraversalHandler
            _automaticDoors;
        private static readonly HashSet<VillagerEntityBridge> ActiveVillagers = new();

        public static IReadOnlyCollection<VillagerEntityBridge> All => ActiveVillagers;
        public VillagerRole Role => GetAssignment().role;
        public VillagerJobMask AllowedJobs => GetAssignment().allowedJobs;
        public string TownId => assignedTownId;

        public IPersistentEntity PersistentEntity { get; set; }
        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => Version;
        public string VillagerName => villagerName;
        public Entity Entity => _entity;
        public EntityDamageSource DamageSource => EntityDamageSource.Tool;
        public GameObject TargetObject => gameObject;
        public IReadOnlyList<EntityTag> DamageTags => EquippedTool?.DamageTags ?? Array.Empty<EntityTag>();

        public IReadOnlyList<IItemStack> Stacks
        {
            get
            {
                RebuildInventoryView();
                return _inventoryView;
            }
        }

        public IReadOnlyList<IItemStack> EquippedItems
        {
            get
            {
                RebuildEquipmentView();
                return _equipmentView;
            }
        }

        public int Size => GetStats().inventorySize;
        public int OccupiedSlots => Stacks.Count;

        public readonly struct HoverHudData
        {
            public readonly string Name;
            public readonly string Needs;
            public readonly string Job;
            public readonly string Step;

            public HoverHudData(string name, string needs, string job, string step)
            {
                Name = name;
                Needs = needs;
                Job = job;
                Step = step;
            }
        }

        /// <summary>Returns a display-ready snapshot for the shared villager hover HUD.</summary>
        public HoverHudData GetHoverHudData()
        {
            EnsureEntity();
            VillagerStats stats = GetStats();
            VillagerNeeds needs = GetNeeds();
            string needsText =
                $"Health  {needs.health}/{stats.maxHealth}\n" +
                $"Hunger  {ToPercent(needs.hunger, stats.maxHunger)}\n" +
                $"Energy  {ToPercent(needs.energy, stats.maxEnergy)}\n" +
                $"Mana    {ToPercent(needs.mana, stats.maxMana)}";

            if (!HasEntity)
                return new HoverHudData(villagerName, needsText, "None", "Idle");

            EntityManager manager = Manager;
            VillagerState state = manager.GetComponentData<VillagerState>(_entity);
            VillagerJobType jobType = GetCurrentJobType(manager, state);
            return new HoverHudData(
                villagerName,
                needsText,
                jobType == VillagerJobType.None ? "None" : SplitWords(jobType.ToString()),
                GetJobStep(manager, state));
        }

        private VillagerJobType GetCurrentJobType(
            EntityManager manager,
            VillagerState state)
        {
            if (state.mode == VillagerMode.Eating) return VillagerJobType.Eat;
            if (state.mode is VillagerMode.Sleeping or VillagerMode.NightSleeping)
                return VillagerJobType.Sleep;
            if (state.activeJobEntity != Entity.Null &&
                manager.Exists(state.activeJobEntity) &&
                manager.HasComponent<TownJob>(state.activeJobEntity))
            {
                return manager.GetComponentData<TownJob>(state.activeJobEntity).type;
            }

            if (state.activeJobId != 0 && _town?.JobBoard is TownJobBoard board &&
                board.TryGetRecord(state.activeJobId, out TownJobRuntime job))
                return job.Type;
            return VillagerJobType.None;
        }

        private string GetJobStep(EntityManager manager, VillagerState state)
        {
            switch (state.mode)
            {
                case VillagerMode.MovingToTarget:
                    if (state.phase == VillagerWorkPhase.Delivering)
                        return "Returning resources";
                    VillagerJobType movingJob = GetCurrentJobType(manager, state);
                    return movingJob is VillagerJobType.Hunt or VillagerJobType.Defend
                        ? "Chasing target"
                        : "Travelling to target";
                case VillagerMode.MovingToSleep: return "Going to bed";
                case VillagerMode.Working:
                    VillagerOrder order = manager.GetComponentData<VillagerOrder>(_entity);
                    VillagerJobType workingJob = GetCurrentJobType(manager, state);
                    if (workingJob is VillagerJobType.Hunt or VillagerJobType.Defend)
                        return "Attacking target";
                    return order.workRemaining > 0f
                        ? $"Working ({order.workRemaining:0.0}s remaining)"
                        : "Working";
                case VillagerMode.Eating: return "Eating";
                case VillagerMode.Sleeping:
                case VillagerMode.NightSleeping:
                    return _sleepBed != null ? "Sleeping in bed" : "Sheltering indoors";
                case VillagerMode.AwaitingWorldCommit:
                    return state.phase == VillagerWorkPhase.Delivering
                        ? "Storing resources"
                        : "Completing job";
                case VillagerMode.Dead: return "Dead";
                default: return "Waiting for work";
            }
        }

        private static string ToPercent(float value, float maximum) =>
            maximum <= 0f ? "0%" : $"{Mathf.RoundToInt(Mathf.Clamp01(value / maximum) * 100f)}%";

        private static string SplitWords(string value) =>
            System.Text.RegularExpressions.Regex.Replace(value, "([a-z])([A-Z])", "$1 $2");

        public float Hunger
        {
            get
            {
                VillagerStats stats = GetStats();
                return stats.maxHunger <= 0f ? 0f : GetNeeds().hunger / stats.maxHunger;
            }
            set
            {
                VillagerStats stats = GetStats();
                VillagerNeeds needs = GetNeeds();
                needs.hunger = Mathf.Clamp01(value) * stats.maxHunger;
                SetNeeds(needs);
            }
        }

        public float Energy
        {
            get
            {
                VillagerStats stats = GetStats();
                return stats.maxEnergy <= 0f ? 0f : GetNeeds().energy / stats.maxEnergy;
            }
            set
            {
                VillagerStats stats = GetStats();
                VillagerNeeds needs = GetNeeds();
                needs.energy = Mathf.Clamp01(value) * stats.maxEnergy;
                SetNeeds(needs);
            }
        }

        [Inject]
        public void Construct(ItemCatalog catalog, IAttackService attackService,
            IItemStackExplosionService dropService,
            IVillagerWorldAdapter worldAdapter,
            IPathFindingService pathFinder,
            IPathFindingMap pathMap)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _attackService = attackService ?? throw new ArgumentNullException(nameof(attackService));
            _dropService = dropService;
            _worldAdapter = worldAdapter ?? throw new ArgumentNullException(nameof(worldAdapter));
            _pathFinder = pathFinder ?? throw new ArgumentNullException(nameof(pathFinder));
            _pathMap = pathMap ?? throw new ArgumentNullException(nameof(pathMap));
        }

        public void Initialize(int health, float speed, float rate,
            float hungerRate, int capacity, VillagerRole initialRole,
            VillagerJobMask jobs, string name = "Villager")
        {
            maximumHealth = Mathf.Max(1, health);
            movementSpeed = Mathf.Max(0f, speed);
            workRate = Mathf.Max(0f, rate);
            hungerDrainPerSecond = Mathf.Max(0f, hungerRate);
            inventorySize = Mathf.Max(1, capacity);
            role = initialRole;
            allowedJobs = jobs;
            villagerName = string.IsNullOrWhiteSpace(name) ? "Villager" : name.Trim();
        }
        
        private void Awake()
        {
            GetComponent<CircleCollider2D>().radius = 0.25f;
        }

        private void OnEnable()
        {
            ActiveVillagers.Add(this);
            EnemyTargetRegistry.Register(this);
            _enemyRegistered = true;
            _pathCancellation = new CancellationTokenSource();
        }

        private void Start() => EnsureEntity();

        private void Update()
        {
            EnsureEntity();
            EntityManager manager = Manager;
            if (_entity == Entity.Null || !HasWorld || !manager.Exists(_entity)) return;

            if (_town == null && Time.time >= _nextTownResolutionTime)
            {
                _nextTownResolutionTime = Time.time + 1f;
                ResolveTown();
                SynchronizeTownIdentity(manager);
            }

            LocalTransform local = manager.GetComponentData<LocalTransform>(_entity);
            SetWorldPosition(local.Position);
            VillagerState state = manager.GetComponentData<VillagerState>(_entity);
            if (TryClearRemovedJob(state))
                return;
            if (TryCancelJobWithMissingTarget(state))
                return;
            if (TryRefreshCombatTarget(ref state))
                return;
            ApplyCompletedPath(manager, state);
            state = manager.GetComponentData<VillagerState>(_entity);
            UpdateAutomaticDoorTraversal(manager);
            if (state.mode == VillagerMode.MovingToTarget ||
                state.mode == VillagerMode.MovingToSleep)
            {
                VillagerPathState pathState =
                    manager.GetComponentData<VillagerPathState>(_entity);
                if (pathState.pathFailed != 0)
                {
                    if (state.mode == VillagerMode.MovingToSleep)
                    {
                        CancelNightRest(false);
                        _nextSleepAttempt = Time.unscaledTime + 5f;
                        return;
                    }
                    if (state.phase == VillagerWorkPhase.Delivering)
                    {
                        ResetCurrentPath();
                        return;
                    }
                    bool unreachable = pathState.pathFailed == 2;
                    if (unreachable && _town?.JobBoard is TownJobBoard board)
                        board.MarkTargetUnreachable(state.activeJobId);
                    FinishJob(state, false,
                        unreachable
                            ? "No walkable A* path reaches the job target."
                            : "The A* path request failed.",
                        !unreachable);
                    return;
                }

                if (state.phase != VillagerWorkPhase.Delivering &&
                    _town?.JobBoard is TownJobBoard activeBoard &&
                    activeBoard.IsJobTargetInvalid(state.activeJobId))
                {
                    FinishJob(state, false,
                        "The job target is temporarily unreachable.", false);
                    return;
                }

                RequestPathIfNeeded(manager, state);
                PrepareNextDoorTraversal(manager);
            }
            else if (_pendingPath != null)
            {
                CancelPendingPath();
            }

            if (state.mode == VillagerMode.Eating) TryEat();
            else if (state.mode == VillagerMode.NightSleeping && _nightRestActive)
            {
                if (_sleepBed != null && !_sleepBed.TryOccupy(this))
                {
                    CancelNightRest(false);
                    return;
                }
                SetEnemyTargetable(!_sleepingIndoors);
                ApplyBedRecovery(Time.deltaTime);
            }
            else if (state.mode == VillagerMode.AwaitingWorldCommit)
                CommitActiveJob(state);
            else if (state.mode == VillagerMode.Dead)
                HandleDeath();
        }

        private void OnDisable()
        {
            CancelPendingPath(false);
            ActiveVillagers.Remove(this);
            SetEnemyTargetable(false);
            _sleepBed?.Release(this);
            CaptureEntity();
            DestroyEntity();
        }

        private void RequestPathIfNeeded(
            EntityManager manager,
            VillagerState state)
        {
            VillagerPathState pathState =
                manager.GetComponentData<VillagerPathState>(_entity);
            DynamicBuffer<VillagerWaypoint> waypoints =
                manager.GetBuffer<VillagerWaypoint>(_entity);
            if (pathState.requestPending != 0 || waypoints.Length > 0 ||
                _pendingPath != null)
            {
                return;
            }

            VillagerOrder order = manager.GetComponentData<VillagerOrder>(_entity);
            Vector2Int start = Vector2Int.FloorToInt(WorldPosition);
            Vector2Int destination = Vector2Int.FloorToInt(
                new Vector2(order.destination.x, order.destination.y));
            bool requiresApproach = state.phase == VillagerWorkPhase.Delivering ||
                state.activeJobEntity != Entity.Null &&
                manager.Exists(state.activeJobEntity) &&
                manager.HasComponent<TownJob>(state.activeJobEntity) &&
                RequiresAdjacentApproach(manager.GetComponentData<TownJob>(
                    state.activeJobEntity).type);
            if (!requiresApproach && start == destination)
            {
                waypoints.Add(new VillagerWaypoint { value = order.destination });
                pathState.waypointIndex = 0;
                pathState.pathFailed = 0;
                manager.SetComponentData(_entity, pathState);
                return;
            }

            ResetPathCancellation();
            VillagerIdentity identity =
                manager.GetComponentData<VillagerIdentity>(_entity);
            PathFindingQuery query = new(
                identity.villagerId.ToString(),
                identity.townId.ToString(),
                string.Empty,
                Array.Empty<string>());

            List<Vector2Int> destinations = requiresApproach
                ? GetApproachCandidates(start, destination, query)
                : new List<Vector2Int> { destination };

            pathState.requestPending = 1;
            pathState.pathFailed = 0;
            pathState.waypointIndex = 0;
            manager.SetComponentData(_entity, pathState);
            _pendingPathJobId = state.activeJobId;
            _pendingPath = FindFirstReachablePathAsync(
                start, destinations, query, requiresApproach,
                _pathCancellation.Token);
        }

        private async Task<WorkPathResult> FindFirstReachablePathAsync(
            Vector2Int start,
            IReadOnlyList<Vector2Int> destinations,
            PathFindingQuery query,
            bool usesApproach,
            CancellationToken cancellationToken)
        {
            for (int i = 0; i < destinations.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Vector2Int candidate = destinations[i];
                List<Vector2Int> path = _pathFinder is
                    IContextualPathFindingService contextual
                    ? await contextual.FindPathAsync(
                        start, candidate, query,
                        Mathf.Max(1, maximumPathVisitedTiles),
                        cancellationToken)
                    : await _pathFinder.FindPathAsync(
                        start, candidate,
                        Mathf.Max(1, maximumPathVisitedTiles),
                        cancellationToken);
                if (path is { Count: > 0 } && path[^1] == candidate)
                    return new WorkPathResult(path, candidate, usesApproach);
            }

            return default;
        }

        private List<Vector2Int> GetApproachCandidates(
            Vector2Int start,
            Vector2Int target,
            PathFindingQuery query)
        {
            var candidates = new List<Vector2Int>(8);
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                if (x == 0 && y == 0) continue;
                Vector2Int candidate = target + new Vector2Int(x, y);
                float cost = _pathMap is IPathTraversalHandler traversal
                    ? traversal.GetEffectiveTraversalCost(candidate, query)
                    : _pathMap.GetTraversalCost(candidate);
                if (!float.IsNaN(cost) && !float.IsInfinity(cost) && cost >= 0f)
                    candidates.Add(candidate);
            }

            candidates.Sort((a, b) =>
            {
                int distance = (a - start).sqrMagnitude.CompareTo(
                    (b - start).sqrMagnitude);
                if (distance != 0) return distance;
                int cardinal = IsCardinal(a - target) ? 0 : 1;
                int otherCardinal = IsCardinal(b - target) ? 0 : 1;
                if (cardinal != otherCardinal)
                    return cardinal.CompareTo(otherCardinal);
                int y = a.y.CompareTo(b.y);
                return y != 0 ? y : a.x.CompareTo(b.x);
            });
            return candidates;
        }

        private static bool IsCardinal(Vector2Int offset) =>
            offset.x == 0 || offset.y == 0;

        private static bool RequiresAdjacentApproach(VillagerJobType type) =>
            type is VillagerJobType.Gather or VillagerJobType.Build or
                VillagerJobType.Craft or VillagerJobType.Farm;

        public void TryBeginNightRest()
        {
            if (_nightRestActive || Time.unscaledTime < _nextSleepAttempt) return;
            EnsureEntity();
            if (!HasEntity) return;
            VillagerState state = Manager.GetComponentData<VillagerState>(_entity);
            if (state.mode == VillagerMode.Dead) return;

            BedComponent assigned = null;
            string villagerId = PersistentEntity?.Id.ToString();
            foreach (BedComponent bed in BedComponent.All)
            {
                if (bed != null && bed.IsIndoors && !bed.IsOccupied &&
                    string.Equals(bed.OwnerVillagerId, villagerId,
                        StringComparison.Ordinal))
                {
                    assigned = bed;
                    break;
                }
            }

            Vector3 destination;
            bool indoors;
            if (assigned != null)
            {
                destination = assigned.GetPosition();
                indoors = true;
            }
            else if (TryFindIndoorShelter(out destination))
                indoors = true;
            else if (_town != null)
            {
                destination = _town.SpawnPoint;
                indoors = false;
            }
            else return;

            ReleaseActiveJobForSleep();
            _sleepBed = assigned;
            _sleepingIndoors = indoors;
            _nightRestActive = true;
            CancelPendingPath();
            DynamicBuffer<VillagerWaypoint> waypoints =
                Manager.GetBuffer<VillagerWaypoint>(_entity);
            waypoints.Clear();
            VillagerPathState path = default;
            Manager.SetComponentData(_entity, path);
            VillagerOrder order = Manager.GetComponentData<VillagerOrder>(_entity);
            order.destination = destination;
            order.hasOrder = 1;
            order.workRemaining = 0f;
            Manager.SetComponentData(_entity, order);
            state.activeJobEntity = Entity.Null;
            state.activeJobId = 0;
            state.mode = VillagerMode.MovingToSleep;
            Manager.SetComponentData(_entity, state);
        }

        public void WakeFromBed()
        {
            if (!_nightRestActive) return;
            CancelNightRest(true);
        }

        public void NotifyBedRemoved(BedComponent bed)
        {
            if (_sleepBed == bed) CancelNightRest(false);
        }

        public void ApplySkippedNightRecovery()
        {
            if (!_nightRestActive || !HasEntity) return;
            VillagerStats stats = GetStats();
            VillagerNeeds needs = GetNeeds();
            needs.health = stats.maxHealth;
            needs.energy = stats.maxEnergy;
            needs.pendingHealthDelta = 0f;
            SetNeeds(needs);
            WakeFromBed();
        }

        private void ApplyBedRecovery(float deltaTime)
        {
            if (!HasEntity || !_sleepingIndoors) return;
            VillagerStats stats = GetStats();
            VillagerNeeds needs = GetNeeds();
            float energyRate = _sleepBed != null
                ? _sleepBed.EnergyPerSecond : stats.restEnergyPerSecond * 0.5f;
            float healthRate = _sleepBed != null
                ? _sleepBed.HealthPerSecond : stats.healthRegenerationPerSecond * 0.5f;
            needs.energy = Mathf.Min(stats.maxEnergy,
                needs.energy + energyRate * deltaTime);
            needs.pendingHealthDelta += healthRate * deltaTime;
            SetNeeds(needs);
        }

        private bool TryFindIndoorShelter(out Vector3 destination)
        {
            destination = default;
            if (_shelterLocator == null || _town == null) return false;
            if (!_shelterLocator.TryFindNearestIndoorPosition(WorldPosition,
                    _town.Position, _town.TownRadius, out Vector2 point)) return false;
            destination = point;
            return true;
        }

        private void ReleaseActiveJobForSleep()
        {
            if (!HasEntity) return;
            VillagerState state = Manager.GetComponentData<VillagerState>(_entity);
            if (state.activeJobEntity != Entity.Null &&
                Manager.Exists(state.activeJobEntity) &&
                Manager.HasComponent<TownJob>(state.activeJobEntity))
            {
                TownJob job = Manager.GetComponentData<TownJob>(state.activeJobEntity);
                job.status = TownJobStatus.Queued;
                job.claimedBy = Entity.Null;
                Manager.SetComponentData(state.activeJobEntity, job);
            }
        }

        private void CancelNightRest(bool wake)
        {
            _sleepBed?.Release(this);
            _sleepBed = null;
            _nightRestActive = false;
            _sleepingIndoors = false;
            SetEnemyTargetable(true);
            if (!HasEntity) return;
            CancelPendingPath();
            Manager.GetBuffer<VillagerWaypoint>(_entity).Clear();
            Manager.SetComponentData(_entity, new VillagerPathState());
            VillagerOrder order = Manager.GetComponentData<VillagerOrder>(_entity);
            order.hasOrder = 0;
            Manager.SetComponentData(_entity, order);
            VillagerState state = Manager.GetComponentData<VillagerState>(_entity);
            if (wake || state.mode is VillagerMode.MovingToSleep or
                VillagerMode.NightSleeping)
                state.mode = VillagerMode.Idle;
            Manager.SetComponentData(_entity, state);
        }

        private void SetEnemyTargetable(bool targetable)
        {
            if (targetable == _enemyRegistered) return;
            if (targetable) EnemyTargetRegistry.Register(this);
            else EnemyTargetRegistry.Unregister(this);
            _enemyRegistered = targetable;
        }

        private void ApplyCompletedPath(
            EntityManager manager,
            VillagerState state)
        {
            if (_pendingPath == null || !_pendingPath.IsCompleted)
                return;

            Task<WorkPathResult> completed = _pendingPath;
            _pendingPath = null;
            _pathCancellation?.Dispose();
            _pathCancellation = null;

            if (_entity == Entity.Null || !manager.Exists(_entity) ||
                state.activeJobId != _pendingPathJobId)
            {
                return;
            }

            VillagerPathState pathState =
                manager.GetComponentData<VillagerPathState>(_entity);
            pathState.requestPending = 0;
            DynamicBuffer<VillagerWaypoint> waypoints =
                manager.GetBuffer<VillagerWaypoint>(_entity);
            waypoints.Clear();

            if (completed.IsCanceled)
            {
                manager.SetComponentData(_entity, pathState);
                return;
            }

            if (completed.IsFaulted)
            {
                Debug.LogException(completed.Exception, this);
                pathState.pathFailed = 1;
                manager.SetComponentData(_entity, pathState);
                return;
            }

            WorkPathResult completedPath = completed.Result;
            List<Vector2Int> result = completedPath.Path;
            if (result == null || result.Count == 0 ||
                result[^1] != completedPath.Destination)
            {
                pathState.pathFailed = 2;
                manager.SetComponentData(_entity, pathState);
                return;
            }

            Vector2Int currentCell = Vector2Int.FloorToInt(WorldPosition);
            foreach (Vector2Int cell in result)
            {
                if (cell == currentCell)
                    continue;
                waypoints.Add(new VillagerWaypoint
                {
                    value = new float3(cell.x + 0.5f, cell.y + 0.5f, 0f)
                });
            }

            VillagerOrder order = manager.GetComponentData<VillagerOrder>(_entity);
            float3 finalPosition = completedPath.UsesApproach
                ? new float3(completedPath.Destination.x + 0.5f,
                    completedPath.Destination.y + 0.5f, 0f)
                : order.destination;
            order.destination = finalPosition;
            manager.SetComponentData(_entity, order);
            if (waypoints.Length == 0 ||
                math.distancesq(waypoints[^1].value, finalPosition) > 0.0001f)
            {
                waypoints.Add(new VillagerWaypoint { value = finalPosition });
            }

            pathState.waypointIndex = 0;
            pathState.pathFailed = 0;
            manager.SetComponentData(_entity, pathState);
        }

        private readonly struct WorkPathResult
        {
            public readonly List<Vector2Int> Path;
            public readonly Vector2Int Destination;
            public readonly bool UsesApproach;

            public WorkPathResult(List<Vector2Int> path,
                Vector2Int destination, bool usesApproach)
            {
                Path = path;
                Destination = destination;
                UsesApproach = usesApproach;
            }
        }

        private void ResetPathCancellation()
        {
            _pathCancellation?.Cancel();
            _pathCancellation?.Dispose();
            _pathCancellation = new CancellationTokenSource();
        }

        private void CancelPendingPath(bool renew = true)
        {
            CancelAutomaticDoorTraversal();
            _pathCancellation?.Cancel();
            _pathCancellation?.Dispose();
            _pathCancellation = renew ? new CancellationTokenSource() : null;
            _pendingPath = null;
            _pendingPathJobId = 0;
        }

        private void PrepareNextDoorTraversal(EntityManager manager)
        {
            if (_automaticDoors == null || !HasEntity) return;
            VillagerPathState pathState =
                manager.GetComponentData<VillagerPathState>(_entity);
            DynamicBuffer<VillagerWaypoint> waypoints =
                manager.GetBuffer<VillagerWaypoint>(_entity);
            if (pathState.requestPending != 0 || pathState.pathFailed != 0 ||
                pathState.waypointIndex >= waypoints.Length) return;

            float3 waypoint = waypoints[pathState.waypointIndex].value;
            if (math.distancesq(
                    manager.GetComponentData<LocalTransform>(_entity).Position,
                    waypoint) > 2.25f) return;

            PathFindingQuery query = CapturePathFindingQuery(manager);
            if (_automaticDoors.TryBeginAutomaticTraversal(
                    query.ActorId,
                    Vector2Int.FloorToInt(new Vector2(waypoint.x, waypoint.y)),
                    query)) return;

            waypoints.Clear();
            manager.SetComponentData(_entity, new VillagerPathState());
            CancelPendingPath();
        }

        private void UpdateAutomaticDoorTraversal(EntityManager manager)
        {
            if (_automaticDoors == null || !HasEntity) return;
            PathFindingQuery query = CapturePathFindingQuery(manager);
            if (string.IsNullOrEmpty(query.ActorId)) return;
            float3 position =
                manager.GetComponentData<LocalTransform>(_entity).Position;
            _automaticDoors.UpdateAutomaticTraversal(
                query.ActorId,
                Vector2Int.FloorToInt(new Vector2(position.x, position.y)));
        }

        private void CancelAutomaticDoorTraversal()
        {
            if (_automaticDoors == null || !HasEntity) return;
            PathFindingQuery query = CapturePathFindingQuery(Manager);
            if (!string.IsNullOrEmpty(query.ActorId))
                _automaticDoors.CancelAutomaticTraversal(query.ActorId);
        }

        private PathFindingQuery CapturePathFindingQuery(EntityManager manager)
        {
            if (_entity == Entity.Null || !manager.Exists(_entity) ||
                !manager.HasComponent<VillagerIdentity>(_entity)) return default;
            VillagerIdentity identity =
                manager.GetComponentData<VillagerIdentity>(_entity);
            return new PathFindingQuery(
                identity.villagerId.ToString(),
                identity.townId.ToString(),
                string.Empty,
                Array.Empty<string>());
        }

        public void SetAssignment(VillagerRole newRole, VillagerJobMask jobs)
        {
            role = newRole;
            allowedJobs = jobs;
            VillagerAssignment assignment = new() { role = newRole, allowedJobs = jobs };
            if (HasEntity) Manager.SetComponentData(_entity, assignment);
            _savedAssignment = assignment;
        }

        private void EnsureEntity()
        {
            EntityManager manager = Manager;
            if (_entity != Entity.Null && HasWorld && manager.Exists(_entity)) return;
            if (!HasWorld) return;

            ResolveTown();
            _entity = manager.CreateEntity(
                typeof(VillagerTag), typeof(VillagerIdentity), typeof(VillagerStats),
                typeof(VillagerNeeds), typeof(VillagerAssignment), typeof(VillagerState),
                typeof(VillagerOrder), typeof(VillagerPathState), typeof(LocalTransform));
            manager.AddBuffer<VillagerInventoryItem>(_entity);
            manager.AddBuffer<VillagerEquippedItem>(_entity);
            manager.AddBuffer<VillagerWaypoint>(_entity);

            string id = PersistentEntity != null
                ? PersistentEntity.Id.ToString()
                : $"runtime-{GetEntityId()}";
            VillagerStats stats = _hasSavedState ? _savedStats : CreateStats();
            VillagerNeeds needs = _hasSavedState
                ? _savedNeeds
                : VillagerDefaults.CreateNeeds(stats.maxHealth);
            VillagerAssignment assignment = _hasSavedState
                ? _savedAssignment
                : new VillagerAssignment { role = role, allowedJobs = allowedJobs };
            manager.SetComponentData(_entity, new VillagerIdentity
            {
                villagerId = new FixedString64Bytes(id),
                townId = new FixedString64Bytes(assignedTownId ?? string.Empty)
            });
            manager.SetComponentData(_entity, stats);
            manager.SetComponentData(_entity, needs);
            manager.SetComponentData(_entity, assignment);
            bool resumeDelivery = needs.health > 0 && _savedPendingDelivery && _town != null;
            manager.SetComponentData(_entity, new VillagerState
            {
                mode = needs.health <= 0
                    ? VillagerMode.Dead
                    : resumeDelivery ? VillagerMode.MovingToTarget : VillagerMode.Idle,
                phase = resumeDelivery ? VillagerWorkPhase.Delivering : VillagerWorkPhase.Work,
                activeJobEntity = Entity.Null
            });
            manager.SetComponentData(_entity, resumeDelivery
                ? new VillagerOrder
                {
                    destination = _town.Position,
                    workRemaining = 0f,
                    hasOrder = 1
                }
                : new VillagerOrder());
            manager.SetComponentData(_entity, LocalTransform.FromPosition(WorldPosition));
            DynamicBuffer<VillagerInventoryItem> inventory = manager.GetBuffer<VillagerInventoryItem>(_entity);
            foreach (ItemStack stack in _savedInventory) AddBufferItem(inventory, stack);
            DynamicBuffer<VillagerEquippedItem> equipment = manager.GetBuffer<VillagerEquippedItem>(_entity);
            foreach (EquippedRecord equipped in _savedEquipment)
                equipment.Add(equipped.ToBuffer());
        }

        private VillagerStats CreateStats()
        {
            VillagerStats value = VillagerDefaults.CreateStats(
                maximumHealth, movementSpeed, workRate,
                hungerDrainPerSecond / 100f, inventorySize);
            value.hungerDrainPerSecond = hungerDrainPerSecond;
            return value;
        }

        private void ResolveTown()
        {
            string id = PersistentEntity?.Id.ToString();
            TownCore best = null;
            float distance = float.PositiveInfinity;
            foreach (TownCore town in TownCoreRegistry.All)
            {
                if (town == null || !town.IsAvailable) continue;
                string candidateId = town.PersistentEntity?.Id.ToString() ?? string.Empty;
                if (!string.IsNullOrEmpty(assignedTownId) && candidateId == assignedTownId)
                {
                    best = town;
                    break;
                }

                if (!string.IsNullOrEmpty(assignedTownId) || !town.ContainsTownPosition(WorldPosition)) continue;
                float candidateDistance = (town.Position - WorldPosition).sqrMagnitude;
                if (candidateDistance < distance)
                {
                    best = town;
                    distance = candidateDistance;
                }
            }

            if (best == null) return;
            if (!string.IsNullOrEmpty(id) && !HasResident(best, id) &&
                !best.TryRegisterResident(id)) return;
            _town = best;
            assignedTownId = best.PersistentEntity?.Id.ToString() ?? assignedTownId;
        }

        private void SynchronizeTownIdentity(EntityManager manager)
        {
            if (_entity == Entity.Null || !manager.Exists(_entity))
                return;

            VillagerIdentity identity = manager.GetComponentData<VillagerIdentity>(_entity);
            identity.townId = new FixedString64Bytes(assignedTownId ?? string.Empty);
            manager.SetComponentData(_entity, identity);
        }

        private Vector3 WorldPosition => PersistentEntity is Component component
            ? component.transform.position
            : transform.position;

        private void SetWorldPosition(float3 position)
        {
            if (PersistentEntity is Component component)
            {
                component.transform.position = position;
                if (component.transform != transform)
                    transform.localPosition = Vector3.zero;
                return;
            }

            transform.position = position;
        }

        private static bool HasResident(TownCore town, string id)
        {
            foreach (string resident in town.ResidentIds)
                if (string.Equals(resident, id, StringComparison.Ordinal))
                    return true;
            return false;
        }

        public int TakeDamage(AttackContext context)
        {
            EnsureEntity();
            VillagerNeeds needs = GetNeeds();
            VillagerStats stats = GetStats();
            int damage = Mathf.Max(0, context.Force - stats.defense);
            int delivered = Mathf.Min(needs.health, damage);
            needs.health -= delivered;
            SetNeeds(needs);
            if (needs.health <= 0 && HasEntity)
            {
                VillagerState state = Manager.GetComponentData<VillagerState>(_entity);
                state.mode = VillagerMode.Dead;
                Manager.SetComponentData(_entity, state);
            }

            return delivered;
        }

        private void HandleDeath()
        {
            if (_deathHandled) return;
            _deathHandled = true;
            List<ItemStackExplosionEntry> drops = new();
            foreach (IItemStack stack in Stacks)
                drops.Add(new ItemStackExplosionEntry(stack.Item, stack.Count, stack.Rarity,
                    stack.Durability, stack.GeneratedData));
            foreach (IItemStack stack in EquippedItems)
                drops.Add(new ItemStackExplosionEntry(stack.Item, stack.Count, stack.Rarity,
                    stack.Durability, stack.GeneratedData));
            _dropService?.Explode(drops, WorldPosition, 2f, 0.25f);
            string id = PersistentEntity?.Id.ToString();
            if (!string.IsNullOrEmpty(id)) _town?.UnregisterResident(id);
            PersistentEntity?.RemoveFromWorld();
        }

        private void CommitActiveJob(VillagerState state)
        {
            if (state.phase == VillagerWorkPhase.Delivering)
            {
                CommitDelivery(state);
                return;
            }
            if (_town?.JobBoard is not TownJobBoard board ||
                !board.TryGetRecord(state.activeJobId, out TownJobRuntime job))
            {
                FinishJob(state, false, "The issuing town is unavailable.");
                return;
            }

            if (job.Type is VillagerJobType.Hunt or VillagerJobType.Defend)
            {
                CommitCombatJob(state, job);
                return;
            }

            bool success = ExecuteJob(job, out string reason);
            if (success && job.Type == VillagerJobType.Gather)
            {
                CollectNearbyPickups();
                if (Stacks.Count > 0)
                {
                    BeginDelivery(state);
                    return;
                }
            }
            FinishJob(state, success, reason);
        }

        private void CommitCombatJob(VillagerState state, TownJobRuntime job)
        {
            GameObject targetObject = ToGameObject(job.Target);
            IHasHealth health = targetObject != null
                ? targetObject.GetComponentInParent<IHasHealth>() ??
                  targetObject.GetComponentInChildren<IHasHealth>()
                : null;
            if (health == null || health.Health <= 0)
            {
                CompleteCombatOrDeliver(state, job);
                return;
            }

            if (!AttackTarget(job.Target, out string reason))
            {
                FinishJob(state, false, reason);
                return;
            }
            if (health.Health <= 0 || job.Target == null)
            {
                CompleteCombatOrDeliver(state, job);
                return;
            }

            Vector3 targetPosition = targetObject.transform.position;
            VillagerOrder order = Manager.GetComponentData<VillagerOrder>(_entity);
            order.destination = targetPosition;
            order.workRemaining = Mathf.Max(0.1f, job.WorkRequired);
            order.hasOrder = 1;
            Manager.SetComponentData(_entity, order);
            state.mode = (targetPosition - WorldPosition).sqrMagnitude <=
                         CombatRange * CombatRange
                ? VillagerMode.Working
                : VillagerMode.MovingToTarget;
            Manager.SetComponentData(_entity, state);
            if (state.mode == VillagerMode.MovingToTarget)
                ResetCurrentPath();
        }

        private bool TryRefreshCombatTarget(ref VillagerState state)
        {
            if (state.phase == VillagerWorkPhase.Delivering) return false;
            if (state.activeJobId == 0 ||
                _town?.JobBoard is not TownJobBoard board ||
                !board.TryGetRecord(state.activeJobId, out TownJobRuntime job) ||
                job.Type is not (VillagerJobType.Hunt or VillagerJobType.Defend))
                return false;

            GameObject targetObject = ToGameObject(job.Target);
            IHasHealth health = targetObject != null
                ? targetObject.GetComponentInParent<IHasHealth>() ??
                  targetObject.GetComponentInChildren<IHasHealth>()
                : null;
            if (health == null || health.Health <= 0)
            {
                CompleteCombatOrDeliver(state, job);
                return true;
            }

            Vector3 targetPosition = targetObject.transform.position;
            bool inRange = job.Type == VillagerJobType.Defend
                ? _town.ContainsTownPosition(targetPosition)
                : _town.ContainsResourcePosition(targetPosition);
            if (!inRange)
            {
                FinishJob(state, false,
                    "The combat target left the town's assigned range.", false);
                return true;
            }

            board.TryUpdateTargetPosition(state.activeJobId, targetPosition);
            if (state.mode == VillagerMode.AwaitingWorldCommit)
                return false;
            VillagerOrder order = Manager.GetComponentData<VillagerOrder>(_entity);
            float distanceSquared = (targetPosition - WorldPosition).sqrMagnitude;
            if (state.mode == VillagerMode.Working &&
                distanceSquared <= CombatRange * CombatRange)
                return false;

            Vector2Int oldCell = Vector2Int.FloorToInt(
                new Vector2(order.destination.x, order.destination.y));
            Vector2Int targetCell = Vector2Int.FloorToInt(targetPosition);
            if (oldCell == targetCell && state.mode == VillagerMode.MovingToTarget)
                return false;

            order.destination = targetPosition;
            order.hasOrder = 1;
            Manager.SetComponentData(_entity, order);
            state.mode = distanceSquared <= CombatRange * CombatRange
                ? VillagerMode.Working
                : VillagerMode.MovingToTarget;
            Manager.SetComponentData(_entity, state);
            if (state.mode == VillagerMode.MovingToTarget)
                ResetCurrentPath();
            return false;
        }

        private void ResetCurrentPath()
        {
            CancelPendingPath();
            if (!HasEntity) return;
            Manager.GetBuffer<VillagerWaypoint>(_entity).Clear();
            Manager.SetComponentData(_entity, new VillagerPathState());
        }

        private bool TryCancelJobWithMissingTarget(VillagerState state)
        {
            if (state.phase == VillagerWorkPhase.Delivering) return false;
            if (state.activeJobId == 0 ||
                _town?.JobBoard is not TownJobBoard board ||
                !board.TryGetRecord(state.activeJobId, out TownJobRuntime job) ||
                !RequiresLiveTarget(job) ||
                job.Target != null)
            {
                return false;
            }

            long jobId = state.activeJobId;
            ClearActiveJobState(state);
            board.TryCancel(jobId);
            return true;
        }

        private bool TryClearRemovedJob(VillagerState state)
        {
            if (state.activeJobId == 0 ||
                _town?.JobBoard is not TownJobBoard board ||
                board.TryGetRecord(state.activeJobId, out _))
                return false;
            ClearActiveJobState(state);
            return true;
        }

        private static bool RequiresLiveTarget(TownJobRuntime job) =>
            job.Type is VillagerJobType.Gather or
                VillagerJobType.Hunt or
                VillagerJobType.Defend or
                VillagerJobType.Craft ||
            job.Type == VillagerJobType.Farm && job.Item == null;

        private bool ExecuteJob(TownJobRuntime job, out string reason)
        {
            reason = string.Empty;
            switch (job.Type)
            {
                case VillagerJobType.Gather:
                    return HarvestTarget(job.Target, out reason);
                case VillagerJobType.Hunt:
                case VillagerJobType.Defend:
                    return AttackTarget(job.Target, out reason);
                case VillagerJobType.Build:
                    return Build(job, out reason);
                case VillagerJobType.Craft:
                    return Craft(job, out reason);
                case VillagerJobType.Farm:
                    return job.Target != null
                        ? Tend(job.Target, out reason)
                        : Build(job, out reason);
                default:
                    reason = "Unsupported villager job.";
                    return false;
            }
        }

        private bool HarvestTarget(UnityEngine.Object target, out string reason)
        {
            GameObject targetObject = ToGameObject(target);
            HarvestableObject harvestable = targetObject != null
                ? targetObject.GetComponentInParent<HarvestableObject>() ??
                  targetObject.GetComponentInChildren<HarvestableObject>(true)
                : null;
            if (harvestable == null)
            {
                reason = "The target is no longer harvestable.";
                return false;
            }

            ToolData tool = EquippedTool;
            var direct = new InteractionContext(
                gameObject,
                InteractionType.Direct,
                tool);
            var toolInteraction = new InteractionContext(
                gameObject,
                InteractionType.Tool,
                tool);
            if (harvestable.CanInteract(direct))
            {
                harvestable.Interact(direct);
                reason = string.Empty;
                return true;
            }
            if (harvestable.CanInteract(toolInteraction))
            {
                harvestable.Interact(toolInteraction);
                reason = string.Empty;
                return true;
            }

            reason = tool == null
                ? "This resource requires a tool the villager has not equipped."
                : "The villager's equipped tool cannot harvest this resource.";
            return false;
        }

        private bool AttackTarget(UnityEngine.Object target, out string reason)
        {
            GameObject targetObject = ToGameObject(target);
            IDamageable damageable = targetObject != null
                ? targetObject.GetComponentInParent<IDamageable>() ?? targetObject.GetComponentInChildren<IDamageable>()
                : null;
            if (damageable == null)
            {
                reason = "The target no longer exists.";
                return false;
            }

            ToolData tool = EquippedTool;
            _attackService.Attack(damageable, new AttackContext(
                gameObject, tool, Mathf.Max(1, GetStats().attack),
                tool != null ? EntityDamageSource.Tool : EntityDamageSource.Unspecified,
                tool?.DamageTags));
            reason = string.Empty;
            return true;
        }

        private bool Build(TownJobRuntime job, out string reason)
        {
            if (job.Item == null)
            {
                reason = "The build order has no buildable item.";
                return false;
            }

            IInventory source = _town?.StockpileInventory ?? this;
            if (!source.TryRemove(job.Item, 1))
            {
                reason = "The town stockpile is missing the building item.";
                return false;
            }

            if (_worldAdapter != null &&
                _worldAdapter.TryBuild(job.Item, job.Position, out reason))
            {
                if (job.Target is TownConstructionQueue construction)
                    construction.CompleteBuild(job.ConstructionId);
                reason = string.Empty;
                return true;
            }

            source.TryAdd(job.Item, 1, out _);
            reason = "The building position is unavailable.";
            return false;
        }

        private bool Craft(TownJobRuntime job, out string reason)
        {
            GameObject target = ToGameObject(job.Target);
            FurnaceComponent furnace = target != null
                ? target.GetComponentInParent<FurnaceComponent>() ??
                  target.GetComponentInChildren<FurnaceComponent>(true)
                : null;
            if (furnace != null)
                return ServiceFurnace(furnace, job.Recipe, out reason);

            CraftingBenchComponent bench =
                target?.GetComponentInParent<CraftingBenchComponent>();
            if (bench == null || job.Recipe == null)
            {
                reason = "The crafting station or recipe is unavailable.";
                return false;
            }

            IInventory inventory = _town?.StockpileInventory ?? this;
            if (!bench.TryCraft(job.Recipe, inventory, inventory, out CraftResult result))
            {
                reason = $"Crafting failed: {result.FailureReason}.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private bool ServiceFurnace(
            FurnaceComponent furnace,
            CraftingRecipeData recipe,
            out string reason)
        {
            reason = string.Empty;
            IInventory inventory = _town?.StockpileInventory ?? this;
            bool collected = furnace.TryCollectAll(inventory, out _);
            if (recipe != null &&
                furnace.TryQueueRecipe(recipe, inventory, out reason))
                return true;
            if (collected)
            {
                reason = string.Empty;
                return true;
            }

            reason = recipe == null
                ? "The furnace has no completed output to collect."
                : string.IsNullOrWhiteSpace(reason)
                    ? "The furnace could not be supplied."
                    : reason;
            return false;
        }

        private bool Tend(UnityEngine.Object target, out string reason)
        {
            PersistentPlant plant = ToGameObject(target)?.GetComponentInParent<PersistentPlant>();
            if (plant == null)
            {
                reason = "The plant no longer exists.";
                return false;
            }

            if (plant.IsMature)
                plant.Interact(new InteractionContext(gameObject, InteractionType.Direct, EquippedTool));
            else
                plant.Water(1f);
            reason = string.Empty;
            return true;
        }

        private void FinishJob(VillagerState state, bool success, string reason,
            bool logFailure = true)
        {
            long id = state.activeJobId;
            ClearActiveJobState(state);
            if (_town?.JobBoard is TownJobBoard board)
            {
                if (success) board.Complete(id);
                else board.Fail(id, reason, logFailure);
            }

        }

        private void CompleteCombatOrDeliver(VillagerState state, TownJobRuntime job)
        {
            CollectNearbyPickups();
            if (job.Type == VillagerJobType.Hunt && Stacks.Count > 0)
                BeginDelivery(state);
            else
                FinishJob(state, true, string.Empty);
        }

        private void BeginDelivery(VillagerState state)
        {
            if (_town == null)
            {
                FinishJob(state, false, "The issuing town is unavailable.");
                return;
            }
            state.phase = VillagerWorkPhase.Delivering;
            state.mode = VillagerMode.MovingToTarget;
            Manager.SetComponentData(_entity, state);
            VillagerOrder order = Manager.GetComponentData<VillagerOrder>(_entity);
            order.destination = _town.Position;
            order.workRemaining = 0f;
            order.hasOrder = 1;
            Manager.SetComponentData(_entity, order);
            ResetCurrentPath();
        }

        private void CommitDelivery(VillagerState state)
        {
            if (!DepositInventory())
            {
                state.mode = VillagerMode.AwaitingWorldCommit;
                Manager.SetComponentData(_entity, state);
                return;
            }
            FinishJob(state, true, string.Empty);
        }

        private void ClearActiveJobState(VillagerState state)
        {
            CancelPendingPath();
            state.activeJobId = 0;
            state.activeJobEntity = Entity.Null;
            state.phase = VillagerWorkPhase.Work;
            state.mode = VillagerMode.Idle;
            Manager.SetComponentData(_entity, state);
            VillagerOrder order = Manager.GetComponentData<VillagerOrder>(_entity);
            order.hasOrder = 0;
            order.workRemaining = 0f;
            Manager.SetComponentData(_entity, order);
            Manager.GetBuffer<VillagerWaypoint>(_entity).Clear();
            Manager.SetComponentData(_entity, new VillagerPathState());
        }

        private void CollectNearbyPickups()
        {
            ItemStackPickup[] pickups = FindObjectsByType<ItemStackPickup>(FindObjectsSortMode.None);
            foreach (ItemStackPickup pickup in pickups)
                if (pickup != null && (pickup.transform.position - WorldPosition).sqrMagnitude <= 6.25f)
                    pickup.Interact(new InteractionContext(gameObject, InteractionType.Direct, EquippedTool));
        }

        private bool DepositInventory()
        {
            IInventory destination = _town?.StockpileInventory;
            if (destination == null) return false;
            var snapshot = new List<IItemStack>(Stacks);
            foreach (IItemStack stack in snapshot)
                TownCoreWindowSection.TryTransfer(this, destination, stack);
            return Stacks.Count == 0;
        }

        private void TryEat()
        {
            IInventory source = FindFood(this) != null ? this : _town?.StockpileInventory;
            ItemData food = FindFood(source);
            if (food == null || !food.TryGetActionData(out IncreaseNeedsActionData data)) return;
            if (!source.TryRemove(food, 1)) return;
            VillagerNeeds needs = GetNeeds();
            VillagerStats stats = GetStats();
            needs.hunger = Mathf.Min(stats.maxHunger, needs.hunger + data.hunger * stats.maxHunger);
            needs.energy = Mathf.Min(stats.maxEnergy, needs.energy + data.energy * stats.maxEnergy);
            SetNeeds(needs);
        }

        private static ItemData FindFood(IInventory inventory)
        {
            if (inventory == null) return null;
            foreach (IItemStack stack in inventory.Stacks)
                if (stack.Item != null && stack.Item.TryGetActionData<IncreaseNeedsActionData>(out _))
                    return stack.Item;
            return null;
        }

        public bool TryEquip(IItemStack source)
        {
            if (source?.Item is not EquipableItemData item || !Contains(source)) return false;
            DynamicBuffer<VillagerEquippedItem> equipped = EquipmentBuffer;
            for (int i = 0; i < equipped.Length; i++)
            {
                if (equipped[i].slot != (byte)item.EquipmentSlot) continue;
                if (!TryResolve(equipped[i].itemId.ToString(), out ItemData previous) ||
                    !TryAdd(previous, 1, out int remainder, (ItemData.Rarity)equipped[i].rarity,
                        equipped[i].durability) || remainder != 0) return false;
                equipped.RemoveAt(i);
                break;
            }

            if (!TryRemove(new ItemStack(source.Item, 1, source.Rarity, source.Durability))) return false;
            equipped.Add(new VillagerEquippedItem
            {
                itemId = new FixedString64Bytes(source.Item.persistentId),
                slot = (byte)item.EquipmentSlot, rarity = (byte)source.Rarity,
                durability = source.Durability
            });
            return true;
        }

        public bool TryUnequip(EquipmentSlot slot)
        {
            DynamicBuffer<VillagerEquippedItem> equipped = EquipmentBuffer;
            for (int i = 0; i < equipped.Length; i++)
            {
                VillagerEquippedItem value = equipped[i];
                if (value.slot != (byte)slot || !TryResolve(value.itemId.ToString(), out ItemData item)) continue;
                if (!TryAdd(item, 1, out int remainder, (ItemData.Rarity)value.rarity, value.durability) ||
                    remainder != 0)
                    return false;
                equipped.RemoveAt(i);
                return true;
            }

            return false;
        }

        private ToolData EquippedTool
        {
            get
            {
                foreach (IItemStack stack in EquippedItems)
                    if (stack.Item.TryGetActionData<ToolHotbarActionData>(out var data) &&
                        data.tool != null)
                        return data.tool;
                return null;
            }
        }

        public bool TryAdd(IItemStack stack, out int remainder) =>
            TryAdd(stack.Item, stack.Count, out remainder, stack.Rarity, stack.Durability);

        public bool TryAdd(ItemData item, int count, out int remainder,
            ItemData.Rarity rarity = ItemData.Rarity.Common, byte durability = byte.MaxValue)
        {
            Inventory value = SnapshotInventory();
            bool result = value.TryAdd(item, count, out remainder, rarity, durability);
            CommitInventory(value);
            return result;
        }

        public bool TryRemove(IItemStack stack) => TryApplyChanges(new[]
            { new InventoryChange(stack.Item, -stack.Count, stack.Rarity, stack.Durability) });

        public bool TryRemove(ItemData item, int count,
            ItemData.Rarity rarity = ItemData.Rarity.Common) =>
            TryApplyChanges(new[] { new InventoryChange(item, -count, rarity) });

        public bool TryRemove(EntityTag tag, int count) => Mutate(value => value.TryRemove(tag, count));

        public bool TryRemoveOne(EntityTag tag, out ItemData item, out ItemData.Rarity rarity)
        {
            Inventory value = SnapshotInventory();
            bool result = value.TryRemoveOne(tag, out item, out rarity);
            if (result) CommitInventory(value);
            return result;
        }

        public int GetCount(ItemData item, ItemData.Rarity rarity = ItemData.Rarity.Common) =>
            SnapshotInventory().GetCount(item, rarity);

        public int GetCount(EntityTag tag) => SnapshotInventory().GetCount(tag);
        public bool Contains(IItemStack stack) => SnapshotInventory().Contains(stack);

        public bool Contains(ItemData item, int count = 1,
            ItemData.Rarity rarity = ItemData.Rarity.Common) => SnapshotInventory().Contains(item, count, rarity);

        public bool Contains(EntityTag tag, int count = 1,
            ItemData.Rarity rarity = ItemData.Rarity.Common) => SnapshotInventory().Contains(tag, count, rarity);

        public bool CanApplyChanges(IReadOnlyList<InventoryChange> changes) =>
            SnapshotInventory().CanApplyChanges(changes);

        public bool TryApplyChanges(IReadOnlyList<InventoryChange> changes)
        {
            Inventory value = SnapshotInventory();
            if (!value.TryApplyChanges(changes)) return false;
            CommitInventory(value);
            return true;
        }

        public void Clear() => CommitInventory(new Inventory(Size));

        private bool Mutate(Func<Inventory, bool> operation)
        {
            Inventory value = SnapshotInventory();
            bool result = operation(value);
            if (result) CommitInventory(value);
            return result;
        }

        private Inventory SnapshotInventory()
        {
            Inventory result = new(Mathf.Max(1, Size));
            foreach (IItemStack stack in Stacks) result.TryAdd(stack, out _);
            return result;
        }

        private void CommitInventory(Inventory inventory)
        {
            DynamicBuffer<VillagerInventoryItem> buffer = InventoryBuffer;
            buffer.Clear();
            foreach (IItemStack stack in inventory.Stacks) AddBufferItem(buffer, stack);
        }

        private static void AddBufferItem(DynamicBuffer<VillagerInventoryItem> buffer, IItemStack stack) =>
            buffer.Add(new VillagerInventoryItem
            {
                itemId = new FixedString64Bytes(stack.Item.persistentId), amount = stack.Count,
                rarity = (byte)stack.Rarity, durability = stack.Durability
            });

        private void RebuildInventoryView()
        {
            _inventoryView.Clear();
            if (!HasEntity) return;
            foreach (VillagerInventoryItem value in InventoryBuffer)
                if (TryResolve(value.itemId.ToString(), out ItemData item))
                    _inventoryView.Add(new ItemStack(item, value.amount,
                        (ItemData.Rarity)value.rarity, value.durability));
        }

        private void RebuildEquipmentView()
        {
            _equipmentView.Clear();
            if (!HasEntity) return;
            foreach (VillagerEquippedItem value in EquipmentBuffer)
                if (TryResolve(value.itemId.ToString(), out ItemData item))
                    _equipmentView.Add(new ItemStack(item, 1,
                        (ItemData.Rarity)value.rarity, value.durability));
        }

        private bool TryResolve(string id, out ItemData item)
        {
            item = null;
            return _catalog != null && _catalog.TryGet(id, out item);
        }

        public void WriteState(BinaryWriter writer)
        {
            CaptureEntity();
            writer.Write(villagerName ?? string.Empty);
            writer.Write(assignedTownId ?? string.Empty);
            WriteStats(writer, _savedStats);
            WriteNeeds(writer, _savedNeeds);
            writer.Write((byte)_savedAssignment.role);
            writer.Write((ushort)_savedAssignment.allowedJobs);
            writer.Write(_savedInventory.Count);
            foreach (ItemStack stack in _savedInventory)
            {
                writer.Write(stack.Item.persistentId);
                writer.Write(stack.Count);
                writer.Write((byte)stack.Rarity);
                writer.Write(stack.Durability);
            }

            writer.Write(_savedEquipment.Count);
            foreach (EquippedRecord item in _savedEquipment)
            {
                writer.Write(item.ItemId);
                writer.Write(item.Slot);
                writer.Write(item.Rarity);
                writer.Write(item.Durability);
            }
            writer.Write(_savedPendingDelivery);
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            if (savedVersion < 1 || savedVersion > Version)
                throw new InvalidDataException($"Unsupported villager version {savedVersion}.");
            villagerName = reader.ReadString();
            assignedTownId = reader.ReadString();
            _savedStats = ReadStats(reader);
            _savedNeeds = ReadNeeds(reader);
            _savedAssignment = new VillagerAssignment
                { role = (VillagerRole)reader.ReadByte(), allowedJobs = (VillagerJobMask)reader.ReadUInt16() };
            _savedInventory.Clear();
            int inventoryCount = reader.ReadInt32();
            if (inventoryCount < 0 || inventoryCount > _savedStats.inventorySize)
                throw new InvalidDataException("Invalid villager inventory count.");
            for (int i = 0; i < inventoryCount; i++)
            {
                string id = reader.ReadString();
                int count = reader.ReadInt32();
                ItemData.Rarity rarity = (ItemData.Rarity)reader.ReadByte();
                byte durability = reader.ReadByte();
                if (!TryResolve(id, out ItemData item))
                    throw new InvalidDataException($"Unknown villager item '{id}'.");
                _savedInventory.Add(new ItemStack(item, count, rarity, durability));
            }

            _savedEquipment.Clear();
            int equipmentCount = reader.ReadInt32();
            if (equipmentCount < 0 || equipmentCount > 32)
                throw new InvalidDataException("Invalid villager equipment count.");
            for (int i = 0; i < equipmentCount; i++)
                _savedEquipment.Add(new EquippedRecord(reader.ReadString(), reader.ReadByte(), reader.ReadByte(),
                    reader.ReadByte()));
            _savedPendingDelivery = savedVersion >= 2 && reader.ReadBoolean();
            _hasSavedState = true;
        }

        public bool IsAtBaseline() => !_hasSavedState && !_savedPendingDelivery && _savedInventory.Count == 0 && _savedEquipment.Count == 0;

        public void OnRemovedFromWorld()
        {
            if (!_deathHandled)
            {
                string id = PersistentEntity?.Id.ToString();
                if (!string.IsNullOrEmpty(id)) _town?.UnregisterResident(id);
            }
        }

        private void CaptureEntity()
        {
            if (!HasEntity) return;
            _savedStats = Manager.GetComponentData<VillagerStats>(_entity);
            _savedNeeds = Manager.GetComponentData<VillagerNeeds>(_entity);
            _savedAssignment = Manager.GetComponentData<VillagerAssignment>(_entity);
            _savedPendingDelivery = Manager.GetComponentData<VillagerState>(_entity).phase ==
                                    VillagerWorkPhase.Delivering;
            _savedInventory.Clear();
            foreach (IItemStack stack in Stacks)
                _savedInventory.Add(new ItemStack(stack.Item, stack.Count, stack.Rarity, stack.Durability));
            _savedEquipment.Clear();
            foreach (VillagerEquippedItem item in EquipmentBuffer)
                _savedEquipment.Add(new EquippedRecord(item.itemId.ToString(), item.slot, item.rarity,
                    item.durability));
            _hasSavedState = true;
        }

        private void DestroyEntity()
        {
            EntityManager manager = Manager;
            if (_entity != Entity.Null && HasWorld && manager.Exists(_entity)) manager.DestroyEntity(_entity);
            _entity = Entity.Null;
        }

        private VillagerStats GetStats()
        {
            EnsureEntity();
            return HasEntity
                ? Manager.GetComponentData<VillagerStats>(_entity)
                : (_hasSavedState ? _savedStats : CreateStats());
        }

        private VillagerAssignment GetAssignment()
        {
            EnsureEntity();
            return HasEntity
                ? Manager.GetComponentData<VillagerAssignment>(_entity)
                : (_hasSavedState
                    ? _savedAssignment
                    : new VillagerAssignment { role = role, allowedJobs = allowedJobs });
        }

        private VillagerNeeds GetNeeds()
        {
            EnsureEntity();
            return HasEntity
                ? Manager.GetComponentData<VillagerNeeds>(_entity)
                : (_hasSavedState ? _savedNeeds : VillagerDefaults.CreateNeeds(maximumHealth));
        }

        private void SetNeeds(VillagerNeeds value)
        {
            EnsureEntity();
            if (HasEntity) Manager.SetComponentData(_entity, value);
            else
            {
                _savedNeeds = value;
                _hasSavedState = true;
            }
        }

        private DynamicBuffer<VillagerInventoryItem> InventoryBuffer =>
            Manager.GetBuffer<VillagerInventoryItem>(_entity);

        private DynamicBuffer<VillagerEquippedItem> EquipmentBuffer => Manager.GetBuffer<VillagerEquippedItem>(_entity);
        private bool HasEntity => _entity != Entity.Null && HasWorld && Manager.Exists(_entity);
        private static bool HasWorld => World.DefaultGameObjectInjectionWorld is { IsCreated: true };

        private static EntityManager Manager
        {
            get
            {
                World world = World.DefaultGameObjectInjectionWorld;
                return world != null && world.IsCreated ? world.EntityManager : default;
            }
        }

        private static GameObject ToGameObject(UnityEngine.Object value)
        {
            // Unity's destroyed-object wrappers still satisfy C# pattern
            // matching, but throw when their gameObject property is read.
            if (value == null)
                return null;
            if (value is GameObject gameObject)
                return gameObject != null ? gameObject : null;
            if (value is Component component)
                return component != null ? component.gameObject : null;
            return null;
        }

        private static void WriteStats(BinaryWriter w, VillagerStats v)
        {
            w.Write(v.maxHealth);
            w.Write(v.maxHunger);
            w.Write(v.maxEnergy);
            w.Write(v.maxMana);
            w.Write(v.workRate);
            w.Write(v.hungerDrainPerSecond);
            w.Write(v.movementEnergyPerSecond);
            w.Write(v.workEnergyPerSecond);
            w.Write(v.restEnergyPerSecond);
            w.Write(v.manaRegenerationPerSecond);
            w.Write(v.healthRegenerationPerSecond);
            w.Write(v.starvationDamagePerSecond);
            w.Write(v.movementSpeed);
            w.Write(v.criticalNeedFraction);
            w.Write(v.inventorySize);
            w.Write(v.attack);
            w.Write(v.defense);
        }

        private static VillagerStats ReadStats(BinaryReader r) => new()
        {
            maxHealth = r.ReadInt32(), maxHunger = r.ReadSingle(), maxEnergy = r.ReadSingle(), maxMana = r.ReadSingle(),
            workRate = r.ReadSingle(), hungerDrainPerSecond = r.ReadSingle(), movementEnergyPerSecond = r.ReadSingle(),
            workEnergyPerSecond = r.ReadSingle(), restEnergyPerSecond = r.ReadSingle(),
            manaRegenerationPerSecond = r.ReadSingle(), healthRegenerationPerSecond = r.ReadSingle(),
            starvationDamagePerSecond = r.ReadSingle(), movementSpeed = r.ReadSingle(),
            criticalNeedFraction = r.ReadSingle(), inventorySize = r.ReadInt32(), attack = r.ReadInt32(),
            defense = r.ReadInt32()
        };

        private static void WriteNeeds(BinaryWriter w, VillagerNeeds v)
        {
            w.Write(v.hunger);
            w.Write(v.energy);
            w.Write(v.health);
            w.Write(v.mana);
            w.Write(v.pendingHealthDelta);
        }

        private static VillagerNeeds ReadNeeds(BinaryReader r) => new()
        {
            hunger = r.ReadSingle(), energy = r.ReadSingle(), health = r.ReadInt32(), mana = r.ReadSingle(),
            pendingHealthDelta = r.ReadSingle()
        };

        private readonly struct EquippedRecord
        {
            public readonly string ItemId;
            public readonly byte Slot, Rarity, Durability;

            public EquippedRecord(string id, byte slot, byte rarity, byte durability)
            {
                ItemId = id;
                Slot = slot;
                Rarity = rarity;
                Durability = durability;
            }

            public VillagerEquippedItem ToBuffer() => new()
                { itemId = new FixedString64Bytes(ItemId), slot = Slot, rarity = Rarity, durability = Durability };
        }
    }
}
