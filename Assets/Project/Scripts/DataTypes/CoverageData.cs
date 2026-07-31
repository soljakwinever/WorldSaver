using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    public enum CoverageColorSource : byte
    {
        CoverageColor,
        BiomeGround,
        BiomeSand,
        BiomeWater,
        BiomeCliff,
        BiomePath
    }

    [CreateAssetMenu(
        fileName = "Coverage",
        menuName = "World Saver/Weather/Coverage")]
    public sealed class CoverageData : ScriptableObject
    {
        [SerializeField] private string coverageId = "coverage";
        [SerializeField] private Sprite coverageSprite;
        [SerializeField] private Color coverageColor = Color.white;
        [SerializeField]
        [Tooltip("Selects the color used by coverage. Biome colors use the current local and seasonal biome blend.")]
        private CoverageColorSource colorSource;
        [SerializeField] private int renderPriority;

        [Header("Simulation")]
        [Tooltip("Tiles this coverage can apply to. Empty allows every ground tile.")]
        [SerializeField] private TileData[] allowedTiles =
            Array.Empty<TileData>();
        [Tooltip("Tiles this coverage cannot apply to. Restrictions take precedence over Allowed Tiles.")]
        [SerializeField] private TileData[] restrictedTiles =
            Array.Empty<TileData>();
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

        [Header("AI Pathing")]
        [SerializeField]
        [Tooltip("Optional traversal hint applied while this coverage is visible.")]
        private TileData.AiPathingTerrain pathingTerrain;
        [SerializeField, Min(1f)]
        private float pathingCost = 1f;
        [SerializeField]
        [Tooltip("Stable immunity ID required to cross hazardous coverage.")]
        private string hazardImmunityId;

        public string CoverageId => string.IsNullOrWhiteSpace(coverageId)
            ? name
            : coverageId.Trim();
        public Sprite CoverageSprite => coverageSprite;
        public Color CoverageColor => coverageColor;
        public CoverageColorSource ColorSource => colorSource;
        public int RenderPriority => renderPriority;
        public TileData[] AllowedTiles =>
            allowedTiles ?? Array.Empty<TileData>();
        public TileData[] RestrictedTiles =>
            restrictedTiles ?? Array.Empty<TileData>();
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
        public TileData.AiPathingTerrain PathingTerrain => pathingTerrain;
        public float PathingCost => Mathf.Max(1f, pathingCost);
        public string HazardImmunityId => hazardImmunityId;
        public string[] AllowedWeatherIds =>
            allowedWeatherIds ?? Array.Empty<string>();
        public string[] AllowedPhaseIds =>
            allowedPhaseIds ?? Array.Empty<string>();
        public string[] RequiredActiveEffectIds =>
            requiredActiveEffectIds ?? Array.Empty<string>();

        public bool AllowsTile(TileData tile)
        {
            if (tile == null)
                return false;

            TileData[] exclusions = RestrictedTiles;
            for (int i = 0; i < exclusions.Length; i++)
            {
                if (exclusions[i] == tile)
                    return false;
            }

            TileData[] restrictions = AllowedTiles;
            if (restrictions.Length == 0)
                return true;

            for (int i = 0; i < restrictions.Length; i++)
            {
                if (restrictions[i] == tile)
                    return true;
            }

            return false;
        }

        private void OnValidate()
        {
            coverageId = coverageId?.Trim();
            hazardImmunityId = hazardImmunityId?.Trim();
            pathingCost = Mathf.Max(1f, pathingCost);
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
