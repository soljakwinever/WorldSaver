using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;

namespace Project.Scripts.Core
{
    public class ChunkPersistenceRoot : MonoBehaviour
    {

        private readonly Dictionary<NodeId, PersistentEntity> _entities = new();
        private readonly Dictionary<NodeId, PersistentEntityRecord> _tombstones = new();

        private Vector2Int _chunkPosition;
        private bool _restoreCompleted;

        public bool RestoreCompleted => _restoreCompleted;

        public void BeginRestore(Vector2Int chunkPosition)
        {
            _chunkPosition = chunkPosition;
            _restoreCompleted = false;

            _entities.Clear();
            _tombstones.Clear();

            RegisterGeneratedEntities();
            SetInteractionEnabled(false);
        }

        public void Restore(
            ChunkState state,
            RuntimeRegion region,
            long currentTick
        )
        {
            if (state == null)
                return;

            foreach (var record in state.entities)
            {
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

            SimulateOffline(state.lastSimulatedTick, currentTick, region);
        }

        public void CompleteRestore()
        {
            _restoreCompleted = true;
            SetInteractionEnabled(true);
        }

        public void FailRestore()
        {
            _restoreCompleted = false;
            SetInteractionEnabled(false);
        }

        public ChunkState Capture(long currentTick)
        {
            if (!_restoreCompleted)
            {
                throw new InvalidOperationException(
                    $"Cannot capture chunk {_chunkPosition} because its" +
                    "peristent state was never restored");
            }

            ChunkState state = new()
            {
                lastSimulatedTick = currentTick
            };

            foreach (PersistentEntityRecord tombstone in _tombstones.Values)
            {
                state.entities.Add(tombstone);
            }

            foreach (PersistentEntity entity in _entities.Values)
            {
                PersistentEntityRecord record = entity.CapturePersistentState(currentTick);

                if (entity.PersistenceKind ==
                    EntityPersistenceKind.RuntimeSpawned ||
                    record.HasPersistentChanges)
                {
                    state.entities.Add(record);
                }
            }

            return state;
        }

        public void NotifyEntityRemoved(PersistentEntity entity)
        {
            _entities.Remove(entity.Id);

            switch (entity.PersistenceKind)
            {
                case EntityPersistenceKind.Procedural:
                case EntityPersistenceKind.Authored:
                    _tombstones[entity.Id] =
                        PersistentEntityRecord.CreateTombstone(entity.Id, entity.PersistenceKind);
                    break;
                case EntityPersistenceKind.RuntimeSpawned:
                    /*
                     * Runtime entities have no procedural baseline.
                     * Removing their record is enough to remove them permanently
                     */
                    _tombstones.Remove(entity.Id);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void RegisterRuntimeEntity(PersistentEntity entity)
        {
            if (_entities.TryAdd(entity.Id, entity))
            {
                throw new InvalidOperationException(
                    $"Entity with id {entity.Id} already exists in chunk {_chunkPosition}");
            }

            _tombstones.Remove(entity.Id);
        }

        public void PrepareForPool()
        {
            _restoreCompleted = false;

            SetInteractionEnabled(false);

            _entities.Clear();
            _tombstones.Clear();

            _chunkPosition = default;
        }

        private void RegisterGeneratedEntities()
        {
            PersistentEntity[] entities = GetComponentsInChildren<PersistentEntity>(true);
            foreach (PersistentEntity entity in entities)
            {
                if (!_entities.TryAdd(entity.Id, entity))
                {
                    Debug.LogError($"Entity with id {entity.Id} already exists in chunk {_chunkPosition}");
                }
            }
        }

        private void RestoreRemovedEntity(PersistentEntityRecord record)
        {
            _tombstones[record.id] = record;

            if (_entities.TryGetValue(record.id, out PersistentEntity entity))
            {
                _entities.Remove(record.id);

                /*
                 * Do not report as a new removal. This is applying an
                 * existing tombstone.
                 */
                entity.SuppressFromPersistentRestore();
            }
        }

        private void RestoreExistingEntity(PersistentEntityRecord record)
        {
            if (_entities.TryGetValue(record.id, out PersistentEntity existing))
            {
                existing.RestorePersistentState(record);
                return;
            }

            if (record.persistenceKind != EntityPersistenceKind.RuntimeSpawned)
            {
                Debug.LogWarning($"Saved entity {record.id} was not found in baseline for chunk {_chunkPosition}");
                return;
            }

            PersistentEntity spawned = null;
            
            spawned.Initialize(record.id, record.persistenceKind);
            
            spawned.RestorePersistentState(record);
            
            _entities.Add(spawned.Id, spawned);
        }

        private void SimulateOffline(long fromTick, long toTick, RuntimeRegion region)
        {
            if(toTick <= fromTick)
                return;

            foreach (PersistentEntity entity in _entities.Values)
            {
                entity.SimulateOffline(fromTick, toTick, region);
            }
        }

        private void SetInteractionEnabled(bool enabled)
        {
            /*
             *Todo: Determine if this is needed.
             * 
             */
        }
    }
}