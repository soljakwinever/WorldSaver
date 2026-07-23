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
        [Inject] private IWorldClock _worldClock;

        private readonly Dictionary<Vector2Int, ActiveChunk> _activeChunks = new();
        private int _nextLoadVersion;
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
