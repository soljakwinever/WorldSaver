using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;

namespace Project.Scripts.Core
{
    public sealed class ChunkPersistenceRoot : MonoBehaviour
    {
        private readonly Dictionary<NodeId, PersistentEntity> _entities = new();
        private readonly Dictionary<NodeId, PersistentEntityRecord> _tombstones = new();

        private Vector2Int _chunkPosition;
        private bool _restoreCompleted;

        public Vector2Int ChunkPosition => _chunkPosition;
        public bool RestoreCompleted => _restoreCompleted;

        // Call this before Chunk.Init spawns any generated nodes.
        public void BeginRestore(Vector2Int chunkPosition)
        {
            _chunkPosition = chunkPosition;
            _restoreCompleted = false;
            _entities.Clear();
            _tombstones.Clear();
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
        }

        public void Restore(ChunkState state)
        {
            if (state?.entities == null)
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

            foreach (PersistentEntity entity in _entities.Values)
                entity.SetPersistenceReady(true);
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

            state.Compact();
            return state;
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
                    _tombstones[entity.Id] =
                        PersistentEntityRecord.CreateTombstone(
                            entity.Id,
                            entity.PersistenceKind);
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

            _entities.Clear();
            _tombstones.Clear();
            _restoreCompleted = false;
            _chunkPosition = default;
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

        private void RestoreRemovedEntity(PersistentEntityRecord record)
        {
            _tombstones[record.id] = record.CreateSnapshot();

            if (!_entities.Remove(record.id, out PersistentEntity entity))
                return;

            entity.SetPersistenceReady(false);
            entity.SuppressFromPersistentRestore();
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
                Debug.LogWarning(
                    $"Runtime entity {record.id} needs an archetype factory. " +
                    "This vertical slice intentionally implements procedural " +
                    "and authored entities first.",
                    this);
                return;
            }

            Debug.LogWarning(
                $"Saved entity {record.id} was not present in the deterministic " +
                $"baseline for chunk {_chunkPosition}.",
                this);
        }
    }
}
