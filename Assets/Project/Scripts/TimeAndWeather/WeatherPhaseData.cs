using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.TimeAndWeather
{
    [CreateAssetMenu(
        fileName = "Weather Phase",
        menuName = "World Saver/Weather/Phase")]
    public sealed class WeatherPhaseData : ScriptableObject
    {
        [SerializeField] private string phaseId = "phase";
        [SerializeField, Min(1)] private int minimumDurationTicks = 30;
        [SerializeField, Min(1)] private int maximumDurationTicks = 90;
        [SerializeField, Range(0f, 1f)] private float baseIntensity = 1f;
        [SerializeField, Range(-2f, 2f)] private float temperatureOffset;
        [SerializeField, Range(-1f, 1f)] private float moistureOffset;
        [SerializeField, ColorUsage(true, true)]
        private Color ambientColorTint = Color.white;
        [SerializeField] private List<WeatherEffectData> effects = new();

        public string PhaseId => string.IsNullOrWhiteSpace(phaseId)
            ? name
            : phaseId.Trim();
        public int MinimumDurationTicks => Mathf.Max(1, minimumDurationTicks);
        public int MaximumDurationTicks =>
            Mathf.Max(MinimumDurationTicks, maximumDurationTicks);
        public float BaseIntensity => Mathf.Clamp01(baseIntensity);
        public float TemperatureOffset => temperatureOffset;
        public float MoistureOffset => moistureOffset;
        public Color AmbientColorTint => ambientColorTint;
        public IReadOnlyList<WeatherEffectData> Effects => effects;

        private void OnValidate()
        {
            minimumDurationTicks = Mathf.Max(1, minimumDurationTicks);
            maximumDurationTicks =
                Mathf.Max(minimumDurationTicks, maximumDurationTicks);
        }
    }
}
