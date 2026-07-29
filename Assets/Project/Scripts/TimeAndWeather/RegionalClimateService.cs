using System;
using System.Collections.Generic;
using System.Linq;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.TimeAndWeather
{
    public sealed class RegionalClimateService : IRegionalClimateService
    {
        private readonly IWorldGenerator _worldGenerator;
        private readonly ITimeController _time;
        private readonly IWeatherModifierSource _eventModifiers;
        private readonly WeatherSimulationSettings _settings;
        private readonly Dictionary<Vector2Int, RegionalClimateBaseline> _cache =
            new();

        public RegionalClimateService(
            IWorldGenerator worldGenerator,
            ITimeController time,
            IWeatherModifierSource eventModifiers,
            WeatherSimulationSettings settings)
        {
            _worldGenerator = worldGenerator;
            _time = time;
            _eventModifiers = eventModifiers;
            _settings = settings;
        }

        public RegionalClimateBaseline GetBaseline(Vector2Int region)
        {
            if (_cache.TryGetValue(region, out RegionalClimateBaseline baseline))
                return baseline;

            baseline = SampleRegion(region);
            _cache.Add(region, baseline);
            return baseline;
        }

        public bool TryGetBaseline(
            Vector2Int region,
            out RegionalClimateBaseline baseline)
        {
            return _cache.TryGetValue(region, out baseline);
        }

        public ClimateSnapshot GetCurrentSnapshot(Vector2Int region)
        {
            return GetSnapshot(
                region,
                new ClimateContext(_time.Season, _time.Year, _time.DayProgress));
        }

        public bool TryGetCurrentSnapshot(
            Vector2Int region,
            out ClimateSnapshot snapshot)
        {
            if (!_cache.ContainsKey(region))
            {
                snapshot = default;
                return false;
            }

            snapshot = GetCurrentSnapshot(region);
            return true;
        }

        public ClimateSnapshot GetSnapshot(
            Vector2Int region,
            ClimateContext context)
        {
            RegionalClimateBaseline baseline = GetBaseline(region);
            float temperature = baseline.MeanTemperature * 2f - 1f;
            float moisture = baseline.MeanMoisture;

            temperature += _settings.GetSeasonTemperatureOffset(context.Season);
            moisture += _settings.GetSeasonMoistureOffset(context.Season);

            uint yearHash = Hash(
                _worldGenerator.Seed,
                region.x,
                region.y,
                context.Year);
            temperature += SignedUnit(yearHash) *
                           _settings.YearlyTemperatureRange;
            moisture += SignedUnit(Hash(yearHash, 17, 31, 47)) *
                        _settings.YearlyMoistureRange;

            // Warmest around mid-afternoon, coolest before dawn.
            temperature += Mathf.Sin(
                    (context.DayProgress - 0.375f) * Mathf.PI * 2f) *
                _settings.DailyTemperatureRange;

            ClimateModifierAccumulator modifiers = new()
            {
                WeatherWeightMultiplier = 1f
            };
            _eventModifiers.Apply(region, context, ref modifiers);
            temperature += modifiers.TemperatureOffset;
            moisture += modifiers.MoistureOffset;

            return new ClimateSnapshot(
                baseline,
                context,
                temperature,
                moisture,
                baseline.WaterCoverage,
                modifiers.WeatherWeightMultiplier);
        }

        public ClimateSnapshot GetLocalSnapshot(
            Vector2 worldPosition,
            ClimateSnapshot regionalSnapshot)
        {
            int worldX = Mathf.FloorToInt(worldPosition.x);
            int worldY = Mathf.FloorToInt(worldPosition.y);
            TerrainSample terrain =
                _worldGenerator.GetTerrainSample(worldX, worldY);

            // The regional snapshot already contains seasonal, daily, yearly,
            // and event offsets. Replace only its regional terrain mean with
            // the continuous terrain temperature at this world cell.
            return regionalSnapshot.WithTerrainTemperature(
                terrain.temperature);
        }

        public void ClearCache() => _cache.Clear();

        private RegionalClimateBaseline SampleRegion(Vector2Int region)
        {
            int samplesPerAxis = _settings.SamplesPerChunkAxis;
            int regionSize = WorldPartition.RegionSizeInChunks;
            int chunkSize = ChunkBuildResult.ChunkSize;
            int regionChunkX = region.x * regionSize;
            int regionChunkY = region.y * regionSize;

            RunningStatistic temperature = new();
            RunningStatistic moisture = new();
            RunningStatistic elevation = new();
            Dictionary<BiomeData, int> biomeCounts = new();
            int waterCount = 0;
            int cliffCount = 0;

            for (int chunkY = 0; chunkY < regionSize; chunkY++)
            {
                for (int chunkX = 0; chunkX < regionSize; chunkX++)
                {
                    int worldChunkX = regionChunkX + chunkX;
                    int worldChunkY = regionChunkY + chunkY;

                    for (int sampleY = 0; sampleY < samplesPerAxis; sampleY++)
                    {
                        for (int sampleX = 0; sampleX < samplesPerAxis; sampleX++)
                        {
                            float fractionX =
                                (sampleX + 0.5f) / samplesPerAxis;
                            float fractionY =
                                (sampleY + 0.5f) / samplesPerAxis;
                            int worldX = Mathf.FloorToInt(
                                (worldChunkX + fractionX) * chunkSize);
                            int worldY = Mathf.FloorToInt(
                                (worldChunkY + fractionY) * chunkSize);

                            TerrainSample sample =
                                _worldGenerator.GetTerrainSample(worldX, worldY);
                            temperature.Add(sample.temperature);
                            moisture.Add(sample.moisture);
                            elevation.Add(sample.height);

                            if (sample.isWater)
                                waterCount++;
                            if (sample.isCliff)
                                cliffCount++;

                            BiomeData biome =
                                sample.biomeBlend.dominantBiome ?? sample.biome;
                            if (biome != null)
                            {
                                biomeCounts.TryGetValue(
                                    biome,
                                    out int biomeCount);
                                biomeCounts[biome] = biomeCount + 1;
                            }
                        }
                    }
                }
            }

            int count = temperature.Count;
            List<BiomeClimateWeight> biomeWeights = biomeCounts
                .OrderByDescending(pair => pair.Value)
                .Select(pair => new BiomeClimateWeight(
                    pair.Key,
                    count == 0 ? 0f : (float)pair.Value / count))
                .ToList();

            return new RegionalClimateBaseline(
                region,
                count,
                temperature.Mean,
                temperature.Minimum,
                temperature.Maximum,
                temperature.Variance,
                moisture.Mean,
                moisture.Minimum,
                moisture.Maximum,
                moisture.Variance,
                count == 0 ? 0f : (float)waterCount / count,
                elevation.Mean,
                elevation.Variance,
                count == 0 ? 0f : (float)cliffCount / count,
                biomeWeights.Count == 0 ? null : biomeWeights[0].Biome,
                biomeWeights,
                Hash(
                    _worldGenerator.Seed,
                    region.x,
                    region.y,
                    samplesPerAxis));
        }

        private static uint Hash(uint seed, int a, int b, int c)
        {
            uint hash = seed ^ 2166136261u;
            hash = (hash ^ unchecked((uint)a)) * 16777619u;
            hash = (hash ^ unchecked((uint)b)) * 16777619u;
            hash = (hash ^ unchecked((uint)c)) * 16777619u;
            hash ^= hash >> 16;
            hash *= 0x7feb352du;
            hash ^= hash >> 15;
            return hash == 0 ? 0x9e3779b9u : hash;
        }

        private static float SignedUnit(uint hash)
        {
            return (hash / (float)uint.MaxValue) * 2f - 1f;
        }

        private struct RunningStatistic
        {
            private double _sum;
            private double _sumSquares;

            public int Count { get; private set; }
            public float Minimum { get; private set; }
            public float Maximum { get; private set; }
            public float Mean => Count == 0 ? 0f : (float)(_sum / Count);
            public float Variance
            {
                get
                {
                    if (Count == 0)
                        return 0f;
                    double mean = _sum / Count;
                    return Mathf.Max(
                        0f,
                        (float)(_sumSquares / Count - mean * mean));
                }
            }

            public void Add(float value)
            {
                if (Count == 0)
                {
                    Minimum = value;
                    Maximum = value;
                }
                else
                {
                    Minimum = Mathf.Min(Minimum, value);
                    Maximum = Mathf.Max(Maximum, value);
                }

                Count++;
                _sum += value;
                _sumSquares += value * value;
            }
        }
    }
}
