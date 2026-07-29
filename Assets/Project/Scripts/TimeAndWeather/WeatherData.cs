using System.Collections.Generic;
using Project.Scripts.Enums;
using UnityEngine;

namespace Project.Scripts.TimeAndWeather
{
    [CreateAssetMenu(
        fileName = "Weather",
        menuName = "World Saver/Weather/Weather")]
    public sealed class WeatherData : ScriptableObject
    {
        [SerializeField] private string weatherId = "weather";
        [SerializeField] private string displayName = "Weather";
        [SerializeField, Min(0f)] private float selectionWeight = 1f;
        [SerializeField] private WeatherSeasonMask seasons = WeatherSeasonMask.All;
        [SerializeField] private Vector2 temperatureRange = new(-1f, 1f);
        [SerializeField] private Vector2 moistureRange = new(0f, 1f);
        [SerializeField] private Vector2 waterCoverageRange = new(0f, 1f);
        [SerializeField, Min(0)] private int cooldownTicks = 30;
        [SerializeField] private List<WeatherPhaseData> phases = new();

        public string WeatherId => string.IsNullOrWhiteSpace(weatherId)
            ? name
            : weatherId.Trim();
        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? WeatherId
            : displayName;
        public int CooldownTicks => Mathf.Max(0, cooldownTicks);
        public IReadOnlyList<WeatherPhaseData> Phases => phases;

        public float GetSelectionWeight(ClimateSnapshot climate)
        {
            if (!AllowsSeason(climate.Context.Season) ||
                !Contains(temperatureRange, climate.Temperature) ||
                !Contains(moistureRange, climate.Moisture) ||
                !Contains(waterCoverageRange, climate.Baseline.WaterCoverage))
            {
                return 0f;
            }

            return Mathf.Max(0f, selectionWeight) *
                   climate.WeatherWeightMultiplier;
        }

        private bool AllowsSeason(Season season)
        {
            int bit = 1 << (int)season;
            return ((int)seasons & bit) != 0;
        }

        private static bool Contains(Vector2 range, float value)
        {
            float minimum = Mathf.Min(range.x, range.y);
            float maximum = Mathf.Max(range.x, range.y);
            return value >= minimum && value <= maximum;
        }
    }
}
