using System.Collections.Generic;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.TimeAndWeather;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    public sealed class TileCoverageSystem : ITickable
    {
        private readonly Chunkloader _chunkloader;
        private readonly IRegionalWeatherService _weather;
        private readonly IWorldClock _clock;
        private readonly List<Chunk> _loadedChunks = new();
        private readonly Queue<CoverageWork> _pendingWork = new();
        private long _lastTick = -1;

        public int MaxChunksProcessedPerFrame { get; set; } = 2;
        public int PendingChunkUpdates => _pendingWork.Count;

        private readonly struct CoverageWork
        {
            public readonly Chunk Chunk;
            public readonly Vector2Int Position;
            public readonly int Generation;
            public readonly long ElapsedTicks;

            public CoverageWork(
                Chunk chunk,
                long elapsedTicks)
            {
                Chunk = chunk;
                Position = chunk.Position;
                Generation = chunk.CoverageGeneration;
                ElapsedTicks = elapsedTicks;
            }
        }

        public TileCoverageSystem(
            Chunkloader chunkloader,
            IRegionalWeatherService weather,
            IWorldClock clock)
        {
            _chunkloader = chunkloader;
            _weather = weather;
            _clock = clock;
        }

        public void Tick()
        {
            long currentTick = _clock.CurrentTick;
            if (_lastTick < 0)
            {
                _lastTick = currentTick;
            }
            else if (currentTick != _lastTick)
            {
                long elapsed = currentTick - _lastTick;
                _lastTick = currentTick;
                if (elapsed > 0)
                {
                    _chunkloader.CopyLoadedChunks(_loadedChunks);
                    foreach (Chunk chunk in _loadedChunks)
                    {
                        _pendingWork.Enqueue(
                            new CoverageWork(chunk, elapsed));
                    }
                }
            }

            int budget = Mathf.Max(1, MaxChunksProcessedPerFrame);
            while (budget-- > 0 && _pendingWork.Count > 0)
            {
                CoverageWork work = _pendingWork.Dequeue();
                if (work.Chunk == null ||
                    work.Chunk.Position != work.Position ||
                    work.Chunk.CoverageGeneration != work.Generation)
                {
                    continue;
                }

                work.Chunk.AdvanceCoverage(
                    _weather,
                    work.ElapsedTicks);
            }
        }

        public bool TryGetCoverage(
            Vector3Int worldCell,
            CoverageData coverage,
            out float amount)
        {
            amount = 0f;
            return _chunkloader.TryGetLoadedChunk(worldCell, out Chunk chunk) &&
                   chunk.TryGetCoverage(worldCell, coverage, out amount);
        }

        public bool TrySetCoverage(
            Vector3Int worldCell,
            CoverageData coverage,
            float amount)
        {
            return _chunkloader.TryGetLoadedChunk(worldCell, out Chunk chunk) &&
                   chunk.TrySetCoverage(worldCell, coverage, amount);
        }
    }
}
