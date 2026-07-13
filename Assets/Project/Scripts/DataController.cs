using System;
using System.Collections.Generic;
using Project.Scripts.Bus;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    public class DataController : MonoBehaviour, IDataController
    {
        [Inject] private MapSignalBus _mapSignalBus;
        [Inject] private RegionRepository _regionRepository;
        [Inject] private WorldClock _worldClock;

        private readonly Dictionary<Vector2Int, ActiveChunk> _loadedChunks = new ();

        private int _nextLoadVersion;
        private bool _acceptSignals;

        private sealed class ActiveChunk
        {
            public Vector2Int position;
            public ChunkPersistenceRoot persistenceRoot;
            public int loadVersion;
            
            public RuntimeRegion region;
            public bool restoreComplete;
        }
        
        private void OnEnable()
        {
            _acceptSignals = true;
            
            _mapSignalBus.ChunkLoaded += MapSignalBusOnChunkLoaded;
            _mapSignalBus.ChunkUnloaded += MapSignalBusOnChunkUnloaded;
        }
        
        private void OnDisable()
        {
            _acceptSignals = false;
            
            _mapSignalBus.ChunkLoaded+= MapSignalBusOnChunkLoaded;
            _mapSignalBus.ChunkUnloaded -= MapSignalBusOnChunkUnloaded;
        }

        private async void MapSignalBusOnChunkLoaded(Vector2Int chunkPosition, IChunk loaded)
        {
            var chunk = loaded as Chunk;
            
            ChunkPersistenceRoot persistenceRoot =
                chunk.GetComponent<ChunkPersistenceRoot>();

            if (persistenceRoot == null)
            {
                Debug.LogError($"Chunk at position {chunkPosition} does not have a {nameof(ChunkPersistenceRoot)} component");
                return;
            }
            
            int loadedVersion = ++_nextLoadVersion;

            ActiveChunk activeChunk = new()
            {
                position = chunkPosition,
                persistenceRoot = persistenceRoot,
                loadVersion = loadedVersion,
                restoreComplete = false
            };

            if (_loadedChunks.ContainsKey(chunkPosition))
            {
                Debug.LogWarning($"Chunk at position {chunkPosition} already loaded. Replacing.");
            }

            _loadedChunks[chunkPosition] = activeChunk;

            persistenceRoot.BeginRestore(chunkPosition);

            try
            {
                Vector2Int regionPosition =
                    WorldPartition.ChunkToRegion(chunkPosition);

                RuntimeRegion region = await _regionRepository.GetReadyAsync(regionPosition, _worldClock.CurrentTick);

                if (!IsCurrentLoad(chunkPosition, persistenceRoot, loadedVersion))
                {
                    return;
                }

                activeChunk.region = region;

                ushort localChunkIndex = WorldPartition.GetLocalChunkIndex(chunkPosition);

                region.TryGetChunkState(localChunkIndex, out ChunkState savedState);

                persistenceRoot.Restore(savedState, region, _worldClock.CurrentTick);

                activeChunk.restoreComplete = true;
                persistenceRoot.CompleteRestore();
            }
            catch (Exception e)
            {
                Debug.LogException(e, persistenceRoot);

                persistenceRoot.FailRestore();
            }
        }
        
        private void MapSignalBusOnChunkUnloaded(Vector2Int chunkPosition)
        {
            if (!_loadedChunks.TryGetValue(chunkPosition, out ActiveChunk activeChunk))
            {
                Debug.LogWarning($"Chunk at position {chunkPosition} was built but not loaded");
            }
            
            _loadedChunks.Remove(chunkPosition);

            try
            {
                if (!activeChunk.restoreComplete || activeChunk.region)
                {
                    return;
                }

                ChunkState snapshot = activeChunk.persistenceRoot.Capture(_worldClock.CurrentTick);

                CommitChunkSnapshot(
                    activeChunk.region,
                    chunkPosition,
                    snapshot
                );
            }
            catch (Exception e)
            {
                Debug.LogException(e, activeChunk.persistenceRoot);
            }
            finally
            {
                activeChunk.persistenceRoot.PrepareForPool();
            }
        }

        private void CommitChunkSnapshot(
            RuntimeRegion region,
            Vector2Int chunkPosition,
            ChunkState snapshot
        )
        {
            ushort localChunkIndex = WorldPartition.GetLocalChunkIndex(chunkPosition);

            snapshot.localChunkIndex = localChunkIndex;
            snapshot.Compact();

            if (snapshot.HasChanges)
            {
                region.SetChunkState(localChunkIndex, snapshot);
            }
            else
            {
                region.RemoveChunkState(localChunkIndex);
            }

            _regionRepository.MarkDirty(region);
        }

        private bool IsCurrentLoad(Vector2Int chunkPosition, ChunkPersistenceRoot persistenceRoot, int loadVersion)
        {
            if (!_acceptSignals) return false;

            if (!_loadedChunks.TryGetValue(chunkPosition, out ActiveChunk current))
            {
                return false;
            }
            
            return current.loadVersion == loadVersion && persistenceRoot == current.persistenceRoot;
        }

        public Awaitable FlushAsync()
        {
            return _regionRepository.FlushDirtyAsync();
        }
    }
}