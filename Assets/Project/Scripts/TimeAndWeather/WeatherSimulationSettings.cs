using System.Collections.Generic;
using Project.Scripts.Enums;
using UnityEngine;

namespace Project.Scripts.TimeAndWeather
{
    [CreateAssetMenu(
        fileName = "Weather Simulation Settings",
        menuName = "World Saver/Weather/Simulation Settings")]
    public sealed class WeatherSimulationSettings : ScriptableObject
    {
        [Header("Climate sampling")]
        [SerializeField, Range(1, 8)] private int samplesPerChunkAxis = 2;

        [Header("Climate modifiers")]
        [SerializeField] private Vector4 seasonalTemperatureOffsets =
            new(0.05f, 0.2f, -0.05f, -0.25f);
        [SerializeField] private Vector4 seasonalMoistureOffsets =
            new(0.1f, -0.05f, 0.05f, 0f);
        [SerializeField, Range(0f, 0.5f)] private float yearlyTemperatureRange =
            0.05f;
        [SerializeField, Range(0f, 0.5f)] private float yearlyMoistureRange =
            0.03f;
        [SerializeField, Range(0f, 0.5f)] private float dailyTemperatureRange =
            0.08f;

        [Header("Simulation")]
        [SerializeField, Min(1)] private int simulationIntervalTicks = 10;
        [SerializeField, Range(0f, 4f)] private float neighborWeatherInfluence =
            0.35f;
        [SerializeField, Range(0f, 1f)] private float accumulationMeltPerTick =
            0.001f;
        [SerializeField] private List<WeatherData> weather = new();

        public int SamplesPerChunkAxis =>
            Mathf.Clamp(samplesPerChunkAxis, 1, 8);
        public float YearlyTemperatureRange => yearlyTemperatureRange;
        public float YearlyMoistureRange => yearlyMoistureRange;
        public float DailyTemperatureRange => dailyTemperatureRange;
        public int SimulationIntervalTicks =>
            Mathf.Max(1, simulationIntervalTicks);
        public float NeighborWeatherInfluence => neighborWeatherInfluence;
        public float AccumulationMeltPerTick => accumulationMeltPerTick;
        public IReadOnlyList<WeatherData> Weather => weather;

        public float GetSeasonTemperatureOffset(Season season) =>
            GetComponent(seasonalTemperatureOffsets, season);

        public float GetSeasonMoistureOffset(Season season) =>
            GetComponent(seasonalMoistureOffsets, season);

        private static float GetComponent(Vector4 values, Season season)
        {
            return season switch
            {
                Season.Spring => values.x,
                Season.Summer => values.y,
                Season.Autumn => values.z,
                Season.Winter => values.w,
                _ => 0f
            };
        }
    }
}
