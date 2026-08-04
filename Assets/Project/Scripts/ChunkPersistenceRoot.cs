using System;
using System.Collections.Generic;
using System.IO;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using Project.Scripts.Gameplay;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Core
{
    public sealed class ChunkPersistenceRoot : MonoBehaviour
    {
        private readonly Dictionary<NodeId, PersistentEntity> _entities = new();
        private readonly Dictionary<NodeId, PersistentEntity> _suppressedEntities = new();
        private readonly Dictionary<NodeId, PersistentEntityRecord> _tombstones = new();
        private readonly Dictionary<int, TileOverrideData> _tileOverrides = new();
        private readonly Dictionary<int, WallHealthData> _wallHealth = new();

        private Vector2Int _chunkPosition;
        private bool _restoreCompleted;
        private Func<PersistentEntityRecord, PersistentEntity> _runtimeEntityFactory;

        [InjectOptional] private IWorldClock _worldClock;
        [InjectOptional] private WorldData _worldData;

        public Vector2Int ChunkPosition => _chunkPosition;
        public bool RestoreCompleted => _restoreCompleted;
        public IEnumerable<TileOverrideData> TileOverrides => _tileOverrides.Values;
        public IEnumerable<WallHealthData> WallHealth => _wallHealth.Values;

        public void SetRuntimeEntityFactory(
            Func<PersistentEntityRecord, PersistentEntity> factory)
        {
            _runtimeEntityFactory = factory;
        }

        // Call this before Chunk.Init spawns any generated nodes.
        public void BeginRestore(Vector2Int chunkPosition)
        {
            _chunkPosition = chunkPosition;
            _restoreCompleted = false;
            _entities.Clear();
            _suppressedEntities.Clear();
            _tombstones.Clear();
            _tileOverrides.Clear();
            _wallHealth.Clear();
            PersistentTileWater water = GetComponent<PersistentTileWater>();
            if (water == null)
                water = gameObject.AddComponent<PersistentTileWater>();
            float ticksPerHour = Mathf.Max(
                1f,
                (_worldData?.minutesPerDay ?? 24f) * 60f / 24f /
                (_worldClock is WorldClock clock ? clock.SecondsPerTick : 1f));
            water.Initialize(
                chunkPosition,
                GetComponent<IPlantTileContext>(),
                _worldClock?.CurrentTick ?? 0,
                ticksPerHour);
        }

        // Registration is explicit because the node pool is not parented under
        // the chunk in the current WorldSaver scene hierarchy.
        public void RegisterGeneratedEntity(PersistentEntity entity)
        {
            if (entity.PersistenceKind == EntityPersistenceKind.RuntimeSpawned)
                throw new ArgumentException("Use RegisterRuntimeEntity instead.", nameof(entity));

            RegisterEntity(entity);
        }

        public void RegisterRuntimeEntity(PersistentEntity entity)
        {
            if (entity.PersistenceKind != EntityPersistenceKind.RuntimeSpawned)
                throw new ArgumentException("Entity is not runtime-spawned.", nameof(entity));

            RegisterEntity(entity);
            _tombstones.Remove(entity.Id);
            if (_restoreCompleted)
                entity.SetPersistenceReady(true);
        }

        public void Restore(ChunkState state)
        {
            if (state == null)
                return;

            RestoreChunkComponents(state.components);

            if (state.tileOverrides != null)
            {
                foreach (TileOverrideData tileOverride in state.tileOverrides)
                {
                    if (tileOverride != null)
                        _tileOverrides[GetTileKey(
                            tileOverride.localX,
                            tileOverride.localY,
                            tileOverride.layer)] = tileOverride.CreateSnapshot();
                }
            }

            if (state.wallHealth != null)
            {
                foreach (WallHealthData record in state.wallHealth)
                {
                    if (record != null)
                    {
                        _wallHealth[GetWallHealthKey(
                            record.localX,
                            record.localY)] = record.CreateSnapshot();
                    }
                }
            }

            if (state.entities == null)
                return;

            foreach (PersistentEntityRecord record in state.entities)
            {
                if (record == null)
                    continue;

                switch (record.existenceState)
                {
                    case EntityExistenceState.Removed:
                        RestoreRemovedEntity(record);
                        break;

                    case EntityExistenceState.Exists:
                        RestoreExistingEntity(record);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public void CompleteRestore()
        {
            _restoreCompleted = true;

            if (_worldClock != null)
                ProcessRespawns(_worldClock.CurrentTick);
            GetComponent<PersistentTileWater>()?.RefreshAllColors();

            foreach (PersistentEntity entity in _entities.Values)
                entity.SetPersistenceReady(true);
        }

        private void Update()
        {
            if (_restoreCompleted && _worldClock != null)
            {
                ProcessRespawns(_worldClock.CurrentTick);
                GetComponent<PersistentTileWater>()?.Tick(_worldClock.CurrentTick);
            }
        }

        public void ProcessRespawns(long currentTick)
        {
            if (!_restoreCompleted || _tombstones.Count == 0)
                return;

            List<NodeId> ready = null;
            foreach (KeyValuePair<NodeId, PersistentEntityRecord> pair in _tombstones)
            {
                PersistentEntityRecord tombstone = pair.Value;
                if (tombstone.respawnAtTick <= 0 ||
                    currentTick < tombstone.respawnAtTick ||
                    !_suppressedEntities.TryGetValue(pair.Key, out PersistentEntity entity))
                {
                    continue;
                }

                if (!tombstone.respawnInsideTownInfluence &&
                    IsInsideTownInfluence(entity.transform.position))
                {
                    continue;
                }

                ready ??= new List<NodeId>();
                ready.Add(pair.Key);
            }

            if (ready == null)
                return;

            foreach (NodeId id in ready)
                Respawn(id);
        }

        public void SimulateOffline(
            long fromTick,
            long toTick,
            OfflineSimulationPolicy policy)
        {
            if (policy == OfflineSimulationPolicy.None || toTick <= fromTick)
                return;

            MonoBehaviour[] chunkBehaviours =
                GetComponents<MonoBehaviour>();

            foreach (MonoBehaviour behaviour in chunkBehaviours)
            {
                if (behaviour is IOfflineSimulatable simulatable)
                    simulatable.SimulateOffline(fromTick, toTick, policy);
            }

            foreach (PersistentEntity entity in _entities.Values)
            {
                foreach (IPersistentComponent component
                         in entity.GetPersistentComponents())
                {
                    if (component is IOfflineSimulatable simulatable)
                    {
                        simulatable.SimulateOffline(
                            fromTick,
                            toTick,
                            policy);
                    }
                }
            }
        }

        public void FailRestore()
        {
            _restoreCompleted = false;

            foreach (PersistentEntity entity in _entities.Values)
                entity.SetPersistenceReady(false);
        }

        public ChunkState Capture(long currentTick)
        {
            if (!_restoreCompleted)
            {
                throw new InvalidOperationException(
                    $"Cannot capture chunk {_chunkPosition}: restore is incomplete.");
            }

            ChunkState state = new()
            {
                lastSimulatedTick = currentTick
            };

            foreach (PersistentEntityRecord tombstone in _tombstones.Values)
                state.entities.Add(tombstone.CreateSnapshot());

            foreach (PersistentEntity entity in _entities.Values)
            {
                PersistentEntityRecord record =
                    entity.CapturePersistentState(currentTick);

                if (record.MustBeSaved)
                    state.entities.Add(record);
            }

            CaptureChunkComponents(state);

            foreach (TileOverrideData tileOverride in _tileOverrides.Values)
                state.tileOverrides.Add(tileOverride.CreateSnapshot());
            foreach (WallHealthData record in _wallHealth.Values)
                state.wallHealth.Add(record.CreateSnapshot());

            state.Compact();
            return state;
        }

        public void SetTileOverride(TileOverrideData tileOverride)
        {
            if (!_restoreCompleted)
                throw new InvalidOperationException("Cannot edit tiles before chunk restore completes.");

            _tileOverrides[GetTileKey(
                tileOverride.localX,
                tileOverride.localY,
                tileOverride.layer)] = tileOverride.CreateSnapshot();
        }

        public bool RemoveTileOverride(
            byte localX,
            byte localY,
            PersistentTileLayer layer)
        {
            if (!_restoreCompleted)
                throw new InvalidOperationException("Cannot edit tiles before chunk restore completes.");

            return _tileOverrides.Remove(GetTileKey(localX, localY, layer));
        }

        public bool HasTileOverride(
            byte localX,
            byte localY,
            PersistentTileLayer layer)
        {
            return _tileOverrides.ContainsKey(
                GetTileKey(localX, localY, layer));
        }

        public bool TryGetWallHealth(byte localX, byte localY, out byte health)
        {
            if (_wallHealth.TryGetValue(
                    GetWallHealthKey(localX, localY),
                    out WallHealthData record))
            {
                health = record.health;
                return true;
            }

            health = default;
            return false;
        }

        public void SetWallHealth(byte localX, byte localY, byte health)
        {
            if (!_restoreCompleted)
            {
                throw new InvalidOperationException(
                    "Cannot edit wall health before chunk restore completes.");
            }

            _wallHealth[GetWallHealthKey(localX, localY)] =
                new WallHealthData
                {
                    localX = localX,
                    localY = localY,
                    health = health
                };
        }

        public bool RemoveWallHealth(byte localX, byte localY)
        {
            if (!_restoreCompleted)
            {
                throw new InvalidOperationException(
                    "Cannot edit wall health before chunk restore completes.");
            }

            return _wallHealth.Remove(GetWallHealthKey(localX, localY));
        }

        public void NotifyEntityRemoved(PersistentEntity entity)
        {
            if (!_entities.Remove(entity.Id))
            {
                Debug.LogWarning(
                    $"Entity {entity.Id} is not registered with chunk {_chunkPosition}.",
                    entity);
                return;
            }

            entity.SetPersistenceReady(false);

            switch (entity.PersistenceKind)
            {
                case EntityPersistenceKind.Procedural:
                case EntityPersistenceKind.Authored:
                    IGeneratedEntityRespawn respawn = null;
                    foreach (MonoBehaviour behaviour in entity.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (behaviour is IGeneratedEntityRespawn candidate)
                        {
                            respawn = candidate;
                            break;
                        }
                    }

                    long removedAtTick = _worldClock?.CurrentTick ?? 0;
                    _tombstones[entity.Id] =
                        PersistentEntityRecord.CreateTombstone(
                            entity.Id,
                            entity.PersistenceKind,
                            respawn?.GetRespawnTick(removedAtTick) ?? 0,
                            respawn?.RespawnInsideTownInfluence ?? false);
                    _suppressedEntities[entity.Id] = entity;
                    break;

                case EntityPersistenceKind.RuntimeSpawned:
                    // Runtime entities have no deterministic baseline. Omitting
                    // their full record is sufficient to remove them forever.
                    _tombstones.Remove(entity.Id);
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // Must be called by DataController before Chunk.Pool despawns its nodes.
        public void PrepareForPool()
        {
            foreach (PersistentEntity entity in _entities.Values)
            {
                entity.SetPersistenceReady(false);
                entity.ClearOwner(this);
            }

            foreach (PersistentEntity entity in _suppressedEntities.Values)
            {
                entity.SetPersistenceReady(false);
                entity.ClearOwner(this);
            }

            _entities.Clear();
            _suppressedEntities.Clear();
            _tombstones.Clear();
            _tileOverrides.Clear();
            _wallHealth.Clear();
            _restoreCompleted = false;
            _chunkPosition = default;
        }

        private static int GetTileKey(
            byte localX,
            byte localY,
            PersistentTileLayer layer)
        {
            return ((int)layer << 16) | (localY << 8) | localX;
        }

        private void RegisterEntity(PersistentEntity entity)
        {
            if (!_entities.TryAdd(entity.Id, entity))
            {
                throw new InvalidOperationException(
                    $"Duplicate entity ID {entity.Id} in chunk {_chunkPosition}.");
            }

            entity.SetOwner(this);
            entity.SetPersistenceReady(false);
        }

        private static int GetWallHealthKey(byte localX, byte localY) =>
            (localY << 8) | localX;

        private void CaptureChunkComponents(ChunkState state)
        {
            HashSet<ushort> capturedTypes = new();
            foreach (MonoBehaviour behaviour in GetComponents<MonoBehaviour>())
            {
                if (behaviour is not IPersistentComponent component)
                    continue;

                if (!capturedTypes.Add(component.PersistentTypeId))
                {
                    Debug.LogError(
                        $"Chunk {_chunkPosition} has duplicate persistent " +
                        $"component type {component.PersistentTypeId}.",
                        this);
                    continue;
                }

                if (component.IsAtBaseline())
                    continue;

                using MemoryStream stream = new();
                using (BinaryWriter writer = new(
                           stream,
                           System.Text.Encoding.UTF8,
                           leaveOpen: true))
                {
                    component.WriteState(writer);
                }

                state.components.Add(new PersistenceComponentRecord
                {
                    typeId = component.PersistentTypeId,
                    version = component.PersistentVersion,
                    data = stream.ToArray()
                });
            }
        }

        private void RestoreChunkComponents(
            List<PersistenceComponentRecord> records)
        {
            if (records == null || records.Count == 0)
                return;

            Dictionary<ushort, IPersistentComponent> components = new();
            foreach (MonoBehaviour behaviour in GetComponents<MonoBehaviour>())
            {
                if (behaviour is not IPersistentComponent component)
                    continue;

                if (!components.TryAdd(
                        component.PersistentTypeId,
                        component))
                {
                    Debug.LogError(
                        $"Chunk {_chunkPosition} has duplicate persistent " +
                        $"component type {component.PersistentTypeId}.",
                        this);
                }
            }

            foreach (PersistenceComponentRecord saved in records)
            {
                if (saved == null ||
                    !components.TryGetValue(
                        saved.typeId,
                        out IPersistentComponent component))
                {
                    continue;
                }

                using MemoryStream stream =
                    new(saved.data ?? Array.Empty<byte>(), writable: false);
                using BinaryReader reader = new(stream);
                component.ReadState(reader, saved.version);
            }
        }

        private void RestoreRemovedEntity(PersistentEntityRecord record)
        {
            _tombstones[record.id] = record.CreateSnapshot();

            if (!_entities.Remove(record.id, out PersistentEntity entity))
                return;

            entity.SetPersistenceReady(false);
            _suppressedEntities[record.id] = entity;
            entity.SuppressFromPersistentRestore();
        }

        private void Respawn(NodeId id)
        {
            if (!_suppressedEntities.Remove(id, out PersistentEntity entity))
                return;

            _tombstones.Remove(id);
            _entities.Add(id, entity);
            foreach (MonoBehaviour behaviour in entity.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour is IEntityRespawnHandler handler)
                    handler.OnRespawned();
            }

            entity.gameObject.SetActive(true);
            entity.SetPersistenceReady(true);
        }

        private static bool IsInsideTownInfluence(Vector3 position)
        {
            foreach (TownCore town in TownCoreRegistry.All)
            {
                if (town != null && town.IsAvailable &&
                    town.ContainsTownPosition(position))
                {
                    return true;
                }
            }

            return false;
        }

        private void RestoreExistingEntity(PersistentEntityRecord record)
        {
            if (_entities.TryGetValue(record.id, out PersistentEntity existing))
            {
                existing.RestorePersistentState(record);
                return;
            }

            if (record.persistenceKind == EntityPersistenceKind.RuntimeSpawned)
            {
                if (_runtimeEntityFactory == null)
                {
                    Debug.LogError($"No runtime entity factory for chunk {_chunkPosition}.", this);
                    return;
                }

                PersistentEntity restored = _runtimeEntityFactory(record);
                if (restored == null)
                    return;

                RegisterRuntimeEntity(restored);
                restored.RestorePersistentState(record);
                return;
            }

            Debug.LogWarning(
                $"Saved entity {record.id} was not present in the deterministic " +
                $"baseline for chunk {_chunkPosition}.",
                this);
        }
    }
}
