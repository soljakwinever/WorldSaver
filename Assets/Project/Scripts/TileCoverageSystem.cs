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
        private readonly Dictionary<Vector2Int, WeatherSample> _samples = new();
        private long _lastTick = -1;

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
            if (currentTick == _lastTick)
                return;

            if (_lastTick < 0)
            {
                _lastTick = currentTick;
                return;
            }

            long elapsed = currentTick - _lastTick;
            _lastTick = currentTick;
            if (elapsed <= 0)
                return;

            _samples.Clear();
            _chunkloader.CopyLoadedChunks(_loadedChunks);
            foreach (Chunk chunk in _loadedChunks)
            {
                Vector2Int region =
                    DataTypes.SaveData.WorldPartition.ChunkToRegion(
                        chunk.Position);
                if (!_samples.TryGetValue(region, out WeatherSample sample))
                {
                    sample = _weather.GetRegionSample(region);
                    _samples.Add(region, sample);
                }

                chunk.AdvanceCoverage(sample, elapsed);
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
