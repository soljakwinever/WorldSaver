using System;
using System.Collections.Generic;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    public sealed class DataController : MonoBehaviour
    {
        [Inject] private IRegionRepository _regions;
        [Inject] private IRegionSimulationService _regionSimulation;
        [Inject] private IWorldClock _worldClock;
        [Inject] private Chunkloader _chunkloader;

        [SerializeField]
        private OfflineSimulationPolicy offlineSimulationPolicy =
            OfflineSimulationPolicy.CatchUp;
        [Min(1)]
        [SerializeField] private int regionalSimulationIntervalTicks = 60;
        [Min(0)]
        [SerializeField] private int regionalSimulationRadius = 1;
        [Min(1)]
        [SerializeField] private int regionalRegionsPerPass = 2;

        private readonly Dictionary<Vector2Int, ActiveChunk> _activeChunks = new();
        private readonly HashSet<RuntimeRegion> _simulatedRegions = new();
        private readonly Queue<Vector2Int> _regionalSimulationQueue = new();
        private int _nextLoadVersion;
        private long _nextRegionalSimulationTick;
        private bool _regionalSimulationInProgress;
        private bool _saveRequested;
        private bool _saveInProgress;

        private sealed class ActiveChunk
        {
            public Chunk chunk;
            public ChunkPersistenceRoot root;
            public RuntimeRegion region;
            public int loadVersion;
            public bool restoreComplete;
        }

        private void OnApplicationQuit()
        {
            RequestSave();
        }

        private void Update()
        {
            long currentTick = _worldClock.CurrentTick;

            if (!_regionalSimulationInProgress &&
                currentTick >= _nextRegionalSimulationTick)
            {
                if (_regionalSimulationQueue.Count == 0)
                {
                    _nextRegionalSimulationTick =
                        checked(currentTick + regionalSimulationIntervalTicks);
                    QueueNearbyRegions();
                }

                SimulateQueuedRegionsAsync();
            }
        }

        public async Awaitable RestoreChunkAsync(Chunk chunk)
        {
            Vector2Int position = chunk.Position;
            ChunkPersistenceRoot root =
                chunk.GetComponent<ChunkPersistenceRoot>();

            if (root == null)
            {
                throw new InvalidOperationException(
                    $"Chunk {position} has no {nameof(ChunkPersistenceRoot)}.");
            }

            if (root.ChunkPosition != position)
            {
                throw new InvalidOperationException(
                    $"Chunk {position} did not call BeginRestore before registration.");
            }

            int version = ++_nextLoadVersion;
            ActiveChunk active = new()
            {
                chunk = chunk,
                root = root,
                loadVersion = version
            };

            _activeChunks[position] = active;

            try
            {
                Vector2Int regionPosition =
                    WorldPartition.ChunkToRegion(position);

                RuntimeRegion region = await _regions.GetReadyAsync(
                    regionPosition,
                    _worldClock.CurrentTick);

                if (!IsCurrent(position, root, version))
                    return;

                active.region = region;

                ushort localIndex =
                    WorldPartition.GetLocalChunkIndex(position);

                region.TryGetChunkState(localIndex, out ChunkState state);
                root.Restore(state);

                long currentTick = _worldClock.CurrentTick;
                long regionFromTick =
                    await SimulateRegionAsync(region, currentTick);
                SimulateChunk(root, state, regionFromTick, currentTick);

                root.CompleteRestore();
                active.restoreComplete = true;
            }
            catch (Exception exception)
            {
                if (IsCurrent(position, root, version))
                    root.FailRestore();

                Debug.LogException(exception, root);
                throw;
            }
        }

        public void CaptureBeforeUnload(Chunk chunk)
        {
            Vector2Int position = chunk.Position;
            ChunkPersistenceRoot root =
                chunk.GetComponent<ChunkPersistenceRoot>();

            if (!_activeChunks.Remove(position, out ActiveChunk active))
            {
                root?.PrepareForPool();
                return;
            }

            try
            {
                if (active.restoreComplete && active.region != null)
                    CaptureIntoRegion(position, active);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, active.root);
            }
            finally
            {
                active.root.PrepareForPool();
            }
        }

        public async Awaitable SaveAsync()
        {
            _worldClock.Save();

            // Capture currently loaded chunks as well as chunks that happened to
            // unload since the previous save.
            ActiveChunk[] active = new ActiveChunk[_activeChunks.Count];
            _activeChunks.Values.CopyTo(active, 0);

            foreach (ActiveChunk chunk in active)
            {
                if (chunk.restoreComplete && chunk.region != null)
                    CaptureIntoRegion(chunk.chunk.Position, chunk);
            }

            await _regions.FlushDirtyAsync();
        }

        // Chunk pool callbacks are synchronous. Coalesce their disk flushes into
        // one asynchronous save operation while preserving every later request.
        public void RequestSave()
        {
            _saveRequested = true;

            if (!_saveInProgress)
                FlushRequestedSavesAsync();
        }

        private async void FlushRequestedSavesAsync()
        {
            _saveInProgress = true;

            try
            {
                while (_saveRequested)
                {
                    _saveRequested = false;
                    await SaveAsync();
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            finally
            {
                _saveInProgress = false;

                if (_saveRequested)
                    FlushRequestedSavesAsync();
            }
        }

        private void CaptureIntoRegion(
            Vector2Int position,
            ActiveChunk active)
        {
            ushort localIndex = WorldPartition.GetLocalChunkIndex(position);
            ChunkState snapshot = active.root.Capture(_worldClock.CurrentTick);
            snapshot.localChunkIndex = localIndex;
            snapshot.Compact();

            if (snapshot.HasChanges)
                active.region.SetChunkState(localIndex, snapshot);
            else
                active.region.RemoveChunkState(localIndex);

            _regions.MarkDirty(active.region);
        }

        private async Awaitable<long> SimulateRegionAsync(
            RuntimeRegion region,
            long currentTick)
        {
            long fromTick = region.LastSimulatedTick;

            if (!_simulatedRegions.Add(region))
                return fromTick;

            if (currentTick <= fromTick)
                return fromTick;

            bool applied = await _regionSimulation.SimulateAsync(
                    region,
                    currentTick,
                    offlineSimulationPolicy);
            if (applied)
            {
                _regions.MarkDirty(region);
            }
            else
            {
                // A concurrent chunk capture or live simulation invalidated
                // the detached result. Allow a later restore to retry.
                _simulatedRegions.Remove(region);
            }

            return fromTick;
        }

        private void SimulateChunk(
            ChunkPersistenceRoot root,
            ChunkState state,
            long regionFromTick,
            long currentTick)
        {
            if (offlineSimulationPolicy == OfflineSimulationPolicy.None ||
                state == null)
            {
                return;
            }

            long fromTick = state.lastSimulatedTick;

            // Version-one saves and early version-two saves can contain a
            // changed chunk without a chunk timestamp. The region timestamp is
            // the safest available lower bound for those records.
            if (fromTick <= 0)
                fromTick = regionFromTick;

            root.SimulateOffline(
                fromTick,
                currentTick,
                offlineSimulationPolicy);
        }

        private void QueueNearbyRegions()
        {
            _regionalSimulationQueue.Clear();

            Vector2Int center =
                WorldPartition.ChunkToRegion(_chunkloader.Position);

            for (int radius = 0; radius <= regionalSimulationRadius; radius++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    for (int x = -radius; x <= radius; x++)
                    {
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) != radius)
                            continue;

                        _regionalSimulationQueue.Enqueue(
                            center + new Vector2Int(x, y));
                    }
                }
            }
        }

        private async void SimulateQueuedRegionsAsync()
        {
            _regionalSimulationInProgress = true;

            try
            {
                int processed = 0;
                long currentTick = _worldClock.CurrentTick;

                while (_regionalSimulationQueue.Count > 0 &&
                       processed < regionalRegionsPerPass)
                {
                    Vector2Int position =
                        _regionalSimulationQueue.Dequeue();
                    RuntimeRegion region =
                        await _regions.GetReadyAsync(position, currentTick);

                    if (await _regionSimulation.SimulateAsync(
                            region,
                            currentTick,
                            OfflineSimulationPolicy.Regional))
                    {
                        _regions.MarkDirty(region);
                    }

                    processed++;
                }

                if (_regionalSimulationQueue.Count > 0)
                    _nextRegionalSimulationTick = currentTick + 1;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            finally
            {
                _regionalSimulationInProgress = false;
            }
        }

        private bool IsCurrent(
            Vector2Int position,
            ChunkPersistenceRoot root,
            int version)
        {
            return _activeChunks.TryGetValue(position, out ActiveChunk current) &&
                   current.loadVersion == version &&
                   current.root == root;
        }
    }
}
