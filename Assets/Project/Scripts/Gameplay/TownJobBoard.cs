using System;
using System.Collections.Generic;
using System.IO;
using Project.Scripts.DataTypes;
using Project.Scripts.Entities;
using Project.Scripts.Interface;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class TownJobBoard : MonoBehaviour, ITownJobBoard,
        IPersistentComponent
    {
        public const ushort TypeId = 0x544A; // TJ
        private const ushort Version = 2;
        private const int MaximumPersistedJobs = 10000;
        [SerializeField, Min(1f)] private float invalidTargetSeconds = 60f;

        private readonly List<JobRecord> _records = new();
        private readonly List<TownJobView> _views = new();
        private readonly Dictionary<VillagerJobType, TownJobPriority> _priorities = new();
        private readonly Dictionary<InvalidTargetKey, InvalidTargetEntry>
            _invalidTargets = new();
        private TownCore _town;
        private long _nextJobId = 1;
        private ItemCatalog _catalog;

        [Inject]
        public void Construct(ItemCatalog catalog) => _catalog = catalog;

        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => Version;
        public IReadOnlyList<TownJobView> Jobs { get { RebuildViews(); return _views; } }

        private void Awake()
        {
            _town = GetComponent<TownCore>();
            foreach (VillagerJobType type in Enum.GetValues(typeof(VillagerJobType)))
                if (IsTownJob(type)) _priorities[type] = TownJobPriority.Normal;
        }

        private void OnEnable() => RecreateMirrors();
        private void OnDisable() => DestroyMirrors();
        private void Update()
        {
            PullMirrorState();
            PruneMissingLiveTargets();
        }

        public bool TryIssue(TownJobRequest request, out long jobId)
        {
            jobId = 0;
            if (!IsTownJob(request.Type) ||
                request.Priority == TownJobPriority.Off ||
                request.WorkRequired < 0f ||
                float.IsNaN(request.WorkRequired) ||
                float.IsInfinity(request.WorkRequired))
                return false;
            if (IsTargetInvalid(request.Type, request.Target, request.Position))
                return false;

            jobId = _nextJobId++;
            int handle = TownJobTargetRegistry.Register(
                request.Target, request.Item, request.Recipe);
            JobRecord record = new()
            {
                Id = jobId,
                Type = request.Type,
                Priority = request.Priority,
                Status = TownJobStatus.Queued,
                Position = request.Position,
                WorkRequired = Mathf.Max(0f, request.WorkRequired),
                TargetHandle = handle,
                ItemId = request.Item?.persistentId ?? string.Empty,
                RecipeId = request.Recipe != null ? request.Recipe.name : string.Empty,
                ConstructionId = request.ConstructionId
            };
            _records.Add(record);
            CreateMirror(record);
            return true;
        }

        public bool TryCancel(long jobId)
        {
            JobRecord record = Find(jobId);
            if (record == null) return false;
            Remove(record);
            return true;
        }

        public TownJobPriority GetPriority(VillagerJobType type) =>
            _priorities.TryGetValue(type, out TownJobPriority value)
                ? value : TownJobPriority.Off;

        public void SetPriority(VillagerJobType type, TownJobPriority priority)
        {
            if (!IsTownJob(type)) throw new ArgumentOutOfRangeException(nameof(type));
            _priorities[type] = priority;
        }

        public bool HasActiveConstructionJob(string constructionId)
        {
            if (string.IsNullOrEmpty(constructionId)) return false;
            foreach (JobRecord record in _records)
                if (!IsTerminal(record.Status) &&
                    string.Equals(record.ConstructionId, constructionId,
                        StringComparison.Ordinal)) return true;
            return false;
        }

        public void CancelConstructionJobs(string constructionId)
        {
            if (string.IsNullOrEmpty(constructionId)) return;
            string dependencyPrefix = constructionId.Length > 24
                ? constructionId.Substring(0, 24) + ":"
                : constructionId + ":";
            var matchingIds = new List<long>();
            foreach (JobRecord record in _records)
                if (string.Equals(record.ConstructionId, constructionId,
                         StringComparison.Ordinal) ||
                     record.ConstructionId?.StartsWith(dependencyPrefix,
                         StringComparison.Ordinal) == true)
                    matchingIds.Add(record.Id);
            foreach (long id in matchingIds) TryCancel(id);
        }

        internal bool TryGetRecord(long id, out TownJobRuntime job)
        {
            PullMirrorState();
            JobRecord record = Find(id);
            if (record == null)
            {
                job = default;
                return false;
            }
            TownJobTargetRegistry.TryGet(record.TargetHandle, out TownJobPayload payload);
            job = new TownJobRuntime(record.Type, record.Position,
                record.TargetHandle, payload.Target, payload.Item, payload.Recipe,
                record.ConstructionId, record.WorkRequired);
            return true;
        }

        internal bool TryUpdateTargetPosition(long id, Vector3 position)
        {
            JobRecord record = Find(id);
            if (record == null || !IsFinite(position)) return false;
            record.Position = position;
            EntityManager manager = GetManager();
            if (record.Mirror != Entity.Null && HasWorld &&
                manager.Exists(record.Mirror) &&
                manager.HasComponent<TownJob>(record.Mirror))
            {
                TownJob job = manager.GetComponentData<TownJob>(record.Mirror);
                job.targetPosition = position;
                manager.SetComponentData(record.Mirror, job);
            }
            return true;
        }

        public bool HasActiveTarget(UnityEngine.Object target, VillagerJobType type)
        {
            if (target == null) return false;
            foreach (JobRecord record in _records)
            {
                if (record.Type != type || IsTerminal(record.Status)) continue;
                if (TownJobTargetRegistry.TryGet(record.TargetHandle, out TownJobPayload payload) &&
                    payload.Target == target) return true;
            }
            return false;
        }

        internal int GetActiveIncomingCount(ItemData item)
        {
            if (item == null) return 0;
            int count = 0;
            foreach (JobRecord record in _records)
            {
                if (IsTerminal(record.Status) ||
                    record.Type is not (VillagerJobType.Gather or
                        VillagerJobType.Hunt) ||
                    !string.Equals(record.ItemId, item.persistentId,
                        StringComparison.Ordinal))
                    continue;
                count++;
            }
            return count;
        }

        public bool IsTargetInvalid(VillagerJobType type,
            UnityEngine.Object target, Vector3 position)
        {
            PurgeInvalidTargets();
            return _invalidTargets.ContainsKey(
                InvalidTargetKey.Create(type, target, position));
        }

        internal bool IsJobTargetInvalid(long jobId)
        {
            JobRecord record = Find(jobId);
            if (record == null) return false;
            TownJobTargetRegistry.TryGet(record.TargetHandle, out TownJobPayload payload);
            return IsTargetInvalid(record.Type, payload.Target, record.Position);
        }

        internal bool MarkTargetUnreachable(long jobId)
        {
            JobRecord record = Find(jobId);
            if (record == null) return false;
            TownJobTargetRegistry.TryGet(record.TargetHandle, out TownJobPayload payload);
            InvalidTargetKey key = InvalidTargetKey.Create(
                record.Type, payload.Target, record.Position);
            _invalidTargets[key] = new InvalidTargetEntry(
                payload.Target,
                Time.realtimeSinceStartup + Mathf.Max(1f, invalidTargetSeconds));
            return true;
        }

        public void ClearInvalidTargets() => _invalidTargets.Clear();

        private void PurgeInvalidTargets()
        {
            if (_invalidTargets.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            var expired = new List<InvalidTargetKey>();
            foreach (KeyValuePair<InvalidTargetKey, InvalidTargetEntry> pair in
                     _invalidTargets)
                if (pair.Value.ExpiresAt <= now ||
                    pair.Value.HadTarget && pair.Value.Target == null)
                    expired.Add(pair.Key);
            foreach (InvalidTargetKey key in expired) _invalidTargets.Remove(key);
        }

        internal void Complete(long id)
        {
            JobRecord record = Find(id);
            if (record == null) return;
            Remove(record);
        }

        internal void Fail(long id, string reason, bool logWarning = true)
        {
            JobRecord record = Find(id);
            if (record == null) return;
            if (logWarning && !string.IsNullOrWhiteSpace(reason))
                Debug.LogWarning($"Town job #{id} ({record.Type}) removed: {reason}", this);
            Remove(record);
        }

        private void Remove(JobRecord record)
        {
            if (record == null) return;
            FinishMirror(record);
            TownJobTargetRegistry.Release(record.TargetHandle);
            _records.Remove(record);
        }

        private void PruneMissingLiveTargets()
        {
            var missing = new List<JobRecord>();
            foreach (JobRecord record in _records)
            {
                if (!RequiresLiveTarget(record.Type, record.ItemId)) continue;
                if (!TownJobTargetRegistry.TryGet(record.TargetHandle,
                        out TownJobPayload payload) || payload.Target == null)
                    missing.Add(record);
            }
            foreach (JobRecord record in missing) Remove(record);
        }

        private void PullMirrorState()
        {
            EntityManager manager = GetManager();
            if (!HasWorld) return;
            foreach (JobRecord record in _records)
            {
                if (record.Mirror == Entity.Null || !manager.Exists(record.Mirror) ||
                    !manager.HasComponent<TownJob>(record.Mirror)) continue;
                TownJob value = manager.GetComponentData<TownJob>(record.Mirror);
                record.Status = value.status;
                record.Retries = value.retryCount;
            }
        }

        private void CreateMirror(JobRecord record)
        {
            EntityManager manager = GetManager();
            if (!isActiveAndEnabled || !HasWorld || record.Mirror != Entity.Null ||
                IsTerminal(record.Status)) return;
            record.Mirror = manager.CreateEntity(typeof(TownJobTag), typeof(TownJob));
            manager.SetComponentData(record.Mirror, new TownJob
            {
                jobId = record.Id,
                townId = TownId,
                type = record.Type,
                priority = record.Priority,
                status = record.Status,
                targetPosition = new float3(record.Position.x, record.Position.y, record.Position.z),
                workRequired = record.WorkRequired,
                claimedBy = Entity.Null,
                targetHandle = record.TargetHandle,
                retryCount = record.Retries
                ,constructionId = new FixedString64Bytes(record.ConstructionId ?? string.Empty)
            });
        }

        private void UpdateMirror(JobRecord record)
        {
            EntityManager manager = GetManager();
            if (record.Mirror == Entity.Null || !HasWorld || !manager.Exists(record.Mirror))
            {
                if (!IsTerminal(record.Status)) CreateMirror(record);
                return;
            }
            TownJob job = manager.GetComponentData<TownJob>(record.Mirror);
            job.status = record.Status;
            job.claimedBy = Entity.Null;
            job.retryCount = record.Retries;
            manager.SetComponentData(record.Mirror, job);
        }

        private void FinishMirror(JobRecord record)
        {
            EntityManager manager = GetManager();
            if (record.Mirror != Entity.Null && HasWorld && manager.Exists(record.Mirror))
                manager.DestroyEntity(record.Mirror);
            record.Mirror = Entity.Null;
        }

        private void RecreateMirrors()
        {
            foreach (JobRecord record in _records) CreateMirror(record);
        }

        private void DestroyMirrors()
        {
            EntityManager manager = GetManager();
            foreach (JobRecord record in _records)
            {
                if (record.Mirror != Entity.Null && HasWorld && manager.Exists(record.Mirror))
                    manager.DestroyEntity(record.Mirror);
                record.Mirror = Entity.Null;
            }
        }

        private FixedString64Bytes TownId
        {
            get
            {
                _town ??= GetComponent<TownCore>();
                string id = _town?.PersistentEntity != null
                    ? _town.PersistentEntity.Id.ToString()
                    : string.Empty;
                return new FixedString64Bytes(id);
            }
        }

        private static EntityManager GetManager()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            return world != null && world.IsCreated ? world.EntityManager : default;
        }
        private static bool HasWorld =>
            World.DefaultGameObjectInjectionWorld is { IsCreated: true };

        private JobRecord Find(long id) => _records.Find(record => record.Id == id);
        private static bool IsTownJob(VillagerJobType type) =>
            type >= VillagerJobType.Gather && type <= VillagerJobType.Farm;
        private static bool IsTerminal(TownJobStatus status) =>
            status is TownJobStatus.Completed or TownJobStatus.Cancelled;

        private readonly struct InvalidTargetKey : IEquatable<InvalidTargetKey>
        {
            private readonly VillagerJobType _type;
            private readonly int _targetId;
            private readonly Vector2Int _cell;

            private InvalidTargetKey(VillagerJobType type, int targetId,
                Vector2Int cell)
            { _type = type; _targetId = targetId; _cell = cell; }

            public static InvalidTargetKey Create(VillagerJobType type,
                UnityEngine.Object target, Vector3 position) => new(
                type,
                target != null
                    ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(target)
                    : 0,
                Vector2Int.FloorToInt(position));

            public bool Equals(InvalidTargetKey other) =>
                _type == other._type && _targetId == other._targetId &&
                _cell == other._cell;
            public override bool Equals(object obj) =>
                obj is InvalidTargetKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(
                (int)_type, _targetId, _cell.x, _cell.y);
        }

        private readonly struct InvalidTargetEntry
        {
            public readonly UnityEngine.Object Target;
            public readonly bool HadTarget;
            public readonly float ExpiresAt;
            public InvalidTargetEntry(UnityEngine.Object target, float expiresAt)
            { Target = target; HadTarget = target != null; ExpiresAt = expiresAt; }
        }

        private void RebuildViews()
        {
            PullMirrorState();
            _views.Clear();
            foreach (JobRecord record in _records)
                _views.Add(new TownJobView(record.Id, record.Type, record.Priority,
                    record.Status, record.Position, record.BlockedReason,
                    record.ConstructionId));
        }

        public void WriteState(BinaryWriter writer)
        {
            PullMirrorState();
            writer.Write(_nextJobId);
            writer.Write(_priorities.Count);
            foreach (KeyValuePair<VillagerJobType, TownJobPriority> pair in _priorities)
            { writer.Write((byte)pair.Key); writer.Write((byte)pair.Value); }
            List<JobRecord> active = _records.FindAll(record =>
                !IsTerminal(record.Status) && CanPersist(record));
            writer.Write(active.Count);
            foreach (JobRecord record in active)
            {
                writer.Write(record.Id); writer.Write((byte)record.Type);
                writer.Write((byte)record.Priority); writer.Write((byte)record.Status);
                writer.Write(record.Position.x); writer.Write(record.Position.y); writer.Write(record.Position.z);
                writer.Write(record.WorkRequired); writer.Write(record.ItemId ?? string.Empty);
                writer.Write(record.RecipeId ?? string.Empty); writer.Write(record.Retries);
                writer.Write(record.BlockedReason ?? string.Empty);
                writer.Write(record.ConstructionId ?? string.Empty);
            }
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            if (savedVersion is < 1 or > Version)
                throw new InvalidDataException($"Unsupported Town Job Board version {savedVersion}.");
            DestroyMirrors(); _records.Clear();
            _nextJobId = reader.ReadInt64();
            int priorities = reader.ReadInt32();
            if (priorities < 0 || priorities > 32) throw new InvalidDataException("Invalid job priority count.");
            for (int i = 0; i < priorities; i++)
            {
                VillagerJobType type = (VillagerJobType)reader.ReadByte();
                TownJobPriority priority = (TownJobPriority)reader.ReadByte();
                if (!IsTownJob(type) || !Enum.IsDefined(typeof(TownJobPriority), priority))
                    throw new InvalidDataException("Invalid town work policy.");
                _priorities[type] = priority;
            }
            int jobs = reader.ReadInt32();
            if (jobs < 0 || jobs > MaximumPersistedJobs) throw new InvalidDataException("Invalid town job count.");
            for (int i = 0; i < jobs; i++)
            {
                JobRecord record = new()
                {
                    Id = reader.ReadInt64(), Type = (VillagerJobType)reader.ReadByte(),
                    Priority = (TownJobPriority)reader.ReadByte(), Status = (TownJobStatus)reader.ReadByte(),
                    Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    WorkRequired = reader.ReadSingle(), ItemId = reader.ReadString(),
                    RecipeId = reader.ReadString(), Retries = reader.ReadByte(),
                    BlockedReason = reader.ReadString(),
                    ConstructionId = savedVersion >= 2 ? reader.ReadString() : string.Empty
                };
                if (!IsTownJob(record.Type) || !Enum.IsDefined(typeof(TownJobStatus), record.Status))
                    throw new InvalidDataException("Invalid persisted town job.");
                if (!CanPersist(record))
                    continue;
                if (record.Status is TownJobStatus.Completed or
                    TownJobStatus.Cancelled or TownJobStatus.Blocked)
                    continue;
                if (record.Status is TownJobStatus.Claimed or TownJobStatus.AwaitingWorldCommit)
                    record.Status = TownJobStatus.Queued;
                RestorePayload(record);
                _records.Add(record);
            }
            RecreateMirrors();
        }

        public bool IsAtBaseline() => _records.Count == 0;

        private void RestorePayload(JobRecord record)
        {
            ItemData item = null;
            if (!string.IsNullOrEmpty(record.ItemId))
                _catalog?.TryGet(record.ItemId, out item);
            CraftingRecipeData recipe = null;
            UnityEngine.Object target = null;
            if (!string.IsNullOrEmpty(record.ConstructionId) &&
                record.Type == VillagerJobType.Build)
            {
                _town ??= GetComponent<TownCore>();
                target = _town != null
                    ? _town.GetComponent<TownConstructionQueue>()
                    : null;
            }
            if (!string.IsNullOrEmpty(record.RecipeId))
            {
                _town ??= GetComponent<TownCore>();
                foreach (GameObject building in _town?.Buildings ?? Array.Empty<GameObject>())
                {
                    CraftingBenchComponent bench = building != null
                        ? building.GetComponentInChildren<CraftingBenchComponent>() : null;
                    if (bench != null)
                    {
                        foreach (CraftingRecipeData candidate in bench.Recipes)
                        {
                            if (candidate != null && candidate.name == record.RecipeId)
                            { recipe = candidate; target = bench; break; }
                        }
                    }
                    if (recipe != null) break;

                    FurnaceComponent furnace = building != null
                        ? building.GetComponentInChildren<FurnaceComponent>(true)
                        : null;
                    if (furnace == null) continue;
                    foreach (CraftingRecipeData candidate in furnace.Recipes)
                    {
                        if (candidate != null && candidate.name == record.RecipeId)
                        { recipe = candidate; target = furnace; break; }
                    }
                    if (recipe != null) break;
                }
            }
            record.TargetHandle = TownJobTargetRegistry.Register(target, item, recipe);
        }

        private static bool CanPersist(JobRecord record) =>
            record.Type == VillagerJobType.Build ||
            record.Type == VillagerJobType.Craft &&
            !string.IsNullOrEmpty(record.RecipeId) ||
            record.Type == VillagerJobType.Farm &&
            !string.IsNullOrEmpty(record.ItemId);

        private static bool RequiresLiveTarget(VillagerJobType type,
            string itemId) => type is VillagerJobType.Gather or
            VillagerJobType.Hunt or VillagerJobType.Defend or
            VillagerJobType.Craft ||
            type == VillagerJobType.Farm && string.IsNullOrEmpty(itemId);

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        [Serializable]
        private sealed class JobRecord
        {
            public long Id; public VillagerJobType Type; public TownJobPriority Priority;
            public TownJobStatus Status; public Vector3 Position; public float WorkRequired;
            public int TargetHandle; public string ItemId; public string RecipeId;
            public byte Retries; public string BlockedReason; public string ConstructionId;
            public Entity Mirror;
        }
    }

    internal readonly struct TownJobRuntime
    {
        public readonly VillagerJobType Type; public readonly Vector3 Position;
        public readonly int TargetHandle; public readonly UnityEngine.Object Target;
        public readonly ItemData Item; public readonly CraftingRecipeData Recipe;
        public readonly string ConstructionId;
        public readonly float WorkRequired;
        public TownJobRuntime(VillagerJobType type, Vector3 position, int handle,
            UnityEngine.Object target, ItemData item, CraftingRecipeData recipe,
            string constructionId, float workRequired)
        { Type = type; Position = position; TargetHandle = handle; Target = target; Item = item; Recipe = recipe; ConstructionId = constructionId; WorkRequired = workRequired; }
    }

    internal readonly struct TownJobPayload
    {
        public readonly UnityEngine.Object Target; public readonly ItemData Item;
        public readonly CraftingRecipeData Recipe;
        public TownJobPayload(UnityEngine.Object target, ItemData item, CraftingRecipeData recipe)
        { Target = target; Item = item; Recipe = recipe; }
    }

    internal static class TownJobTargetRegistry
    {
        private static readonly Dictionary<int, TownJobPayload> Values = new();
        private static int _next = 1;
        public static int Register(UnityEngine.Object target, ItemData item, CraftingRecipeData recipe)
        { int id = _next++; Values[id] = new TownJobPayload(target, item, recipe); return id; }
        public static bool TryGet(int id, out TownJobPayload payload) => Values.TryGetValue(id, out payload);
        public static void Release(int id) => Values.Remove(id);
    }
}
