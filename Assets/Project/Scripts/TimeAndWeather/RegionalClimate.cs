using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Enums;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.TimeAndWeather
{
    [Serializable]
    public readonly struct BiomeClimateWeight
    {
        public readonly BiomeData Biome;
        public readonly float Weight;

        public BiomeClimateWeight(BiomeData biome, float weight)
        {
            Biome = biome;
            Weight = weight;
        }
    }

    public sealed class RegionalClimateBaseline
    {
        public Vector2Int Region { get; }
        public int SampleCount { get; }
        public float MeanTemperature { get; }
        public float MinTemperature { get; }
        public float MaxTemperature { get; }
        public float TemperatureVariance { get; }
        public float MeanMoisture { get; }
        public float MinMoisture { get; }
        public float MaxMoisture { get; }
        public float MoistureVariance { get; }
        public float WaterCoverage { get; }
        public float LandCoverage => 1f - WaterCoverage;
        public float MeanElevation { get; }
        public float ElevationVariance { get; }
        public float CliffCoverage { get; }
        public BiomeData DominantBiome { get; }
        public IReadOnlyList<BiomeClimateWeight> BiomeWeights { get; }
        public uint ClimateHash { get; }

        public RegionalClimateBaseline(
            Vector2Int region,
            int sampleCount,
            float meanTemperature,
            float minTemperature,
            float maxTemperature,
            float temperatureVariance,
            float meanMoisture,
            float minMoisture,
            float maxMoisture,
            float moistureVariance,
            float waterCoverage,
            float meanElevation,
            float elevationVariance,
            float cliffCoverage,
            BiomeData dominantBiome,
            IReadOnlyList<BiomeClimateWeight> biomeWeights,
            uint climateHash)
        {
            Region = region;
            SampleCount = sampleCount;
            MeanTemperature = meanTemperature;
            MinTemperature = minTemperature;
            MaxTemperature = maxTemperature;
            TemperatureVariance = temperatureVariance;
            MeanMoisture = meanMoisture;
            MinMoisture = minMoisture;
            MaxMoisture = maxMoisture;
            MoistureVariance = moistureVariance;
            WaterCoverage = waterCoverage;
            MeanElevation = meanElevation;
            ElevationVariance = elevationVariance;
            CliffCoverage = cliffCoverage;
            DominantBiome = dominantBiome;
            BiomeWeights = biomeWeights;
            ClimateHash = climateHash;
        }
    }

    public readonly struct ClimateContext
    {
        public readonly Season Season;
        public readonly int Year;
        public readonly float DayProgress;

        public ClimateContext(Season season, int year, float dayProgress)
        {
            Season = season;
            Year = Mathf.Max(0, year);
            DayProgress = Mathf.Repeat(dayProgress, 1f);
        }
    }

    public struct ClimateModifierAccumulator
    {
        public float TemperatureOffset;
        public float MoistureOffset;
        public float WeatherWeightMultiplier;
    }

    public readonly struct ClimateSnapshot
    {
        public readonly RegionalClimateBaseline Baseline;
        public readonly ClimateContext Context;
        public readonly float Temperature;
        public readonly float Moisture;
        public readonly float WaterInfluence;
        public readonly float WeatherWeightMultiplier;

        public ClimateSnapshot(
            RegionalClimateBaseline baseline,
            ClimateContext context,
            float temperature,
            float moisture,
            float waterInfluence,
            float weatherWeightMultiplier)
        {
            Baseline = baseline;
            Context = context;
            Temperature = Mathf.Clamp(temperature, -1f, 1f);
            Moisture = Mathf.Clamp01(moisture);
            WaterInfluence = Mathf.Clamp01(waterInfluence);
            WeatherWeightMultiplier = Mathf.Max(0f, weatherWeightMultiplier);
        }

        public ClimateSnapshot WithTerrainTemperature(
            float normalizedTerrainTemperature)
        {
            float regionalTerrainTemperature =
                Baseline.MeanTemperature * 2f - 1f;
            float localTerrainTemperature =
                Mathf.Clamp01(normalizedTerrainTemperature) * 2f - 1f;
            return new ClimateSnapshot(
                Baseline,
                Context,
                Temperature +
                localTerrainTemperature -
                regionalTerrainTemperature,
                Moisture,
                WaterInfluence,
                WeatherWeightMultiplier);
        }
    }

    public interface IRegionalClimateService
    {
        RegionalClimateBaseline GetBaseline(Vector2Int region);
        bool TryGetBaseline(
            Vector2Int region,
            out RegionalClimateBaseline baseline);
        ClimateSnapshot GetCurrentSnapshot(Vector2Int region);
        bool TryGetCurrentSnapshot(
            Vector2Int region,
            out ClimateSnapshot snapshot);
        ClimateSnapshot GetSnapshot(Vector2Int region, ClimateContext context);
        ClimateSnapshot GetLocalSnapshot(
            Vector2 worldPosition,
            ClimateSnapshot regionalSnapshot);
        void ClearCache();
    }

    public interface IWeatherModifierSource
    {
        void Apply(
            Vector2Int region,
            ClimateContext context,
            ref ClimateModifierAccumulator modifiers);
    }

    public sealed class NullWeatherModifierSource : IWeatherModifierSource
    {
        public void Apply(
            Vector2Int region,
            ClimateContext context,
            ref ClimateModifierAccumulator modifiers)
        {
        }
    }

    public readonly struct WeatherEffectSample
    {
        public readonly WeatherEffectData Effect;
        public readonly float Intensity;

        public WeatherEffectSample(
            WeatherEffectData effect,
            float intensity)
        {
            Effect = effect;
            Intensity = Mathf.Clamp01(intensity);
        }
    }

    public readonly struct WeatherSample
    {
        public readonly Vector2Int Region;
        public readonly ClimateSnapshot Climate;
        public readonly string WeatherId;
        public readonly string PhaseId;
        public readonly float Intensity;
        public readonly float AmbientTemperature;
        public readonly Color AmbientColorTint;
        public readonly float PuddleAccumulation;
        public readonly float SnowAccumulation;
        public readonly WeatherEffectSample[] ActiveEffects;

        public WeatherSample(
            Vector2Int region,
            ClimateSnapshot climate,
            string weatherId,
            string phaseId,
            float intensity,
            float ambientTemperature,
            Color ambientColorTint,
            float puddleAccumulation,
            float snowAccumulation,
            WeatherEffectSample[] activeEffects = null)
        {
            Region = region;
            Climate = climate;
            WeatherId = weatherId ?? string.Empty;
            PhaseId = phaseId ?? string.Empty;
            Intensity = Mathf.Clamp01(intensity);
            AmbientTemperature = Mathf.Clamp(ambientTemperature, -1f, 1f);
            AmbientColorTint = ambientColorTint;
            PuddleAccumulation = Mathf.Clamp01(puddleAccumulation);
            SnowAccumulation = Mathf.Clamp01(snowAccumulation);
            ActiveEffects = activeEffects ?? Array.Empty<WeatherEffectSample>();
        }

        public WeatherSample WithTerrainTemperature(
            float normalizedTerrainTemperature)
        {
            ClimateSnapshot localClimate =
                Climate.WithTerrainTemperature(normalizedTerrainTemperature);
            return new WeatherSample(
                Region,
                localClimate,
                WeatherId,
                PhaseId,
                Intensity,
                AmbientTemperature +
                localClimate.Temperature -
                Climate.Temperature,
                AmbientColorTint,
                PuddleAccumulation,
                SnowAccumulation,
                ActiveEffects);
        }
    }

    public interface IRegionalWeatherService
    {
        float GlobalTemperatureOffset { get; }
        WeatherSample Sample(Vector2 worldPosition);
        WeatherSample GetRegionSample(Vector2Int region);
        bool TryGetCachedRegionSample(
            Vector2Int region,
            out WeatherSample sample);
        float GetAmbientTemperature(Vector2 worldPosition);
        bool IsWeatherActive(Vector2 worldPosition, string weatherId);
        void SetGlobalTemperatureOffset(float offset);
        Awaitable<bool> TryStartWeatherAsync(
            Vector2Int region,
            string weatherId);
    }

    public interface IWeatherWorldClock
    {
        long CurrentTick { get; }
    }

    internal static class WeatherRegionUtility
    {
        public static Vector2Int WorldToRegion(Vector2 worldPosition)
        {
            return WorldPartition.ChunkToRegion(
                WorldPartition.WorldToChunk(worldPosition));
        }
    }
}
