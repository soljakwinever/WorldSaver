using System.Collections.Generic;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.TimeAndWeather;
using Unity.Profiling;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    public sealed class TileCoverageSystem : ITickable
    {
        private readonly Chunkloader _chunkloader;
        private readonly IRegionalWeatherService _weather;
        private readonly IWorldClock _clock;
        private readonly WorldData _worldData;
        private readonly List<Chunk> _loadedChunks = new();
        private readonly Queue<CoverageWork> _pendingWork = new();
        private readonly Dictionary<Chunk, CoverageWork> _pendingByChunk =
            new();
        private long _lastTick = -1;
        private bool _startupPassScheduled;

        private static readonly ProfilerMarker ScheduleMarker =
            new("TileCoverage.Schedule");
        private static readonly ProfilerMarker ProcessMarker =
            new("TileCoverage.ProcessChunk");

        public int MaxChunksProcessedPerFrame { get; set; } = 2;
        public int PendingChunkUpdates => _pendingWork.Count;

        private sealed class CoverageWork
        {
            public Chunk Chunk;
            public Vector2Int Position;
            public int Generation;
            public long ElapsedTicks;

            public CoverageWork(
                Chunk chunk,
                long elapsedTicks)
            {
                Chunk = chunk;
                Position = chunk.Position;
                Generation = chunk.CoverageGeneration;
                ElapsedTicks = elapsedTicks;
            }

            public void AddElapsedTicks(long elapsedTicks)
            {
                if (elapsedTicks <= 0)
                    return;

                ElapsedTicks = ElapsedTicks > long.MaxValue - elapsedTicks
                    ? long.MaxValue
                    : ElapsedTicks + elapsedTicks;
            }
        }

        public TileCoverageSystem(
            Chunkloader chunkloader,
            IRegionalWeatherService weather,
            IWorldClock clock,
            WorldData worldData)
        {
            _chunkloader = chunkloader;
            _weather = weather;
            _clock = clock;
            _worldData = worldData;
        }

        public void Tick()
        {
            long currentTick = _clock.CurrentTick;
            bool scheduledRegularPass = false;
            if (_lastTick < 0)
            {
                _lastTick = currentTick;
            }
            else if (currentTick != _lastTick)
            {
                long elapsed = currentTick - _lastTick;
                long interval = System.Math.Max(
                    1L, _worldData?.coverageUpdateIntervalTicks ?? 60L);
                if (elapsed > 0 && elapsed >= interval)
                {
                    long intervals = elapsed / interval;
                    long simulatedTicks = intervals > long.MaxValue / interval
                        ? long.MaxValue
                        : intervals * interval;
                    _lastTick = _lastTick > long.MaxValue - simulatedTicks
                        ? currentTick
                        : _lastTick + simulatedTicks;
                    using (ScheduleMarker.Auto())
                    {
                        _chunkloader.CopyLoadedChunks(_loadedChunks);
                        foreach (Chunk chunk in _loadedChunks)
                            Schedule(chunk, simulatedTicks);
                    }
                    scheduledRegularPass = true;
                }
                else if (elapsed < 0)
                {
                    _lastTick = currentTick;
                }
            }

            if (!_startupPassScheduled &&
                _chunkloader.TryCopyVisibleLoadedChunks(_loadedChunks))
            {
                if (!scheduledRegularPass)
                {
                    long interval = System.Math.Max(
                        1L, _worldData?.coverageUpdateIntervalTicks ?? 60L);
                    using (ScheduleMarker.Auto())
                    {
                        foreach (Chunk chunk in _loadedChunks)
                            Schedule(chunk, interval);
                    }
                }
                _startupPassScheduled = true;
            }

            int budget = Mathf.Max(1, MaxChunksProcessedPerFrame);
            while (budget-- > 0 && _pendingWork.Count > 0)
            {
                CoverageWork work = _pendingWork.Dequeue();
                _pendingByChunk.Remove(work.Chunk);
                if (work.Chunk == null ||
                    work.Chunk.Position != work.Position ||
                    work.Chunk.CoverageGeneration != work.Generation)
                {
                    continue;
                }

                using (ProcessMarker.Auto())
                {
                    work.Chunk.AdvanceCoverage(
                        _weather,
                        work.ElapsedTicks);
                }
            }
        }

        private void Schedule(Chunk chunk, long elapsedTicks)
        {
            if (chunk == null)
                return;

            if (_pendingByChunk.TryGetValue(chunk, out CoverageWork pending))
            {
                if (pending.Position == chunk.Position &&
                    pending.Generation == chunk.CoverageGeneration)
                {
                    pending.AddElapsedTicks(elapsedTicks);
                    return;
                }

                pending.Position = chunk.Position;
                pending.Generation = chunk.CoverageGeneration;
                pending.ElapsedTicks = elapsedTicks;
                return;
            }

            CoverageWork work = new(chunk, elapsedTicks);
            _pendingByChunk.Add(chunk, work);
            _pendingWork.Enqueue(work);
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
