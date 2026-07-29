using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(
        fileName = "Coverage",
        menuName = "World Saver/Weather/Coverage")]
    public sealed class CoverageData : ScriptableObject
    {
        [SerializeField] private string coverageId = "coverage";
        [SerializeField] private Sprite coverageSprite;
        [SerializeField] private Color coverageColor = Color.white;
        [SerializeField] private int renderPriority;

        [Header("Simulation")]
        [Tooltip("Coverage persists inside this ambient temperature range. Outside it, coverage decays.")]
        [SerializeField] private Vector2 ambientTemperatureRange =
            new(-1f, 1f);
        [Tooltip("Stable WeatherData IDs that accumulate this coverage. Empty means any weather.")]
        [SerializeField] private string[] allowedWeatherIds =
            Array.Empty<string>();
        [Tooltip("Stable WeatherPhaseData IDs that accumulate this coverage. Empty means any phase.")]
        [SerializeField] private string[] allowedPhaseIds =
            Array.Empty<string>();
        [Tooltip("Active effects that accumulate this coverage. Empty means no required effect.")]
        [SerializeField] private string[] requiredActiveEffectIds =
            Array.Empty<string>();
        [SerializeField, Min(0f)]
        [Tooltip("Normalized coverage added per world tick during matching weather.")]
        private float accumulationRate = 0.001f;
        [SerializeField, Range(0f, 1f)]
        [Tooltip("Deterministic per-tile variation around the accumulation rate. A value of 0.5 gives tiles rates between 50% and 150%.")]
        private float accumulationRateVariation;
        [SerializeField, Min(0f)]
        [Tooltip("Normalized coverage removed per world tick outside the persistence temperature range.")]
        private float decayRate = 0.001f;
        [SerializeField, Range(0f, 1f)]
        [Tooltip("Starting amount when a new tile loads during matching accumulation weather.")]
        private float initialCoverage;
        [SerializeField]
        [Tooltip("When disabled, coverage clears immediately outside its persistence temperature range.")]
        private bool slowlyDecayWhenTemperatureFails = true;

        [Header("GroundTile.mat")]
        [SerializeField] private Vector2 coverageTiling = Vector2.one;

        public string CoverageId => string.IsNullOrWhiteSpace(coverageId)
            ? name
            : coverageId.Trim();
        public Sprite CoverageSprite => coverageSprite;
        public Color CoverageColor => coverageColor;
        public int RenderPriority => renderPriority;
        public float AccumulationRate => accumulationRate;
        public float AccumulationRateVariation =>
            accumulationRateVariation;
        public float DecayRate => decayRate;
        public float InitialCoverage => initialCoverage;
        public bool SlowlyDecayWhenTemperatureFails =>
            slowlyDecayWhenTemperatureFails;
        public Vector2 CoverageTiling => coverageTiling;
        public Vector2 AmbientTemperatureRange =>
            ambientTemperatureRange;
        public string[] AllowedWeatherIds =>
            allowedWeatherIds ?? Array.Empty<string>();
        public string[] AllowedPhaseIds =>
            allowedPhaseIds ?? Array.Empty<string>();
        public string[] RequiredActiveEffectIds =>
            requiredActiveEffectIds ?? Array.Empty<string>();

        private void OnValidate()
        {
            coverageId = coverageId?.Trim();
            NormalizeIds(allowedWeatherIds);
            NormalizeIds(allowedPhaseIds);
            NormalizeIds(requiredActiveEffectIds);
            coverageTiling = new Vector2(
                Mathf.Max(0.0001f, coverageTiling.x),
                Mathf.Max(0.0001f, coverageTiling.y));
        }

        private static void NormalizeIds(string[] ids)
        {
            if (ids == null)
                return;

            for (int i = 0; i < ids.Length; i++)
                ids[i] = ids[i]?.Trim();
        }
    }
}
