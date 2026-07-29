using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "Feature", menuName = "World Generation/Feature")]
    public sealed class FeatureData : ScriptableObject
    {
        [Tooltip("Stable identifier used as part of deterministic feature generation.")]
        public string persistentId = "feature";
        [Min(0f)] public float selectionWeight = 1f;

        [Header("Footprint")]
        [Min(1f)] public float minimumRadius = 24f;
        [Min(1f)] public float maximumRadius = 64f;
        [Min(0.1f)] public float minimumAspect = 0.65f;
        [Min(0.1f)] public float maximumAspect = 1f;
        [Range(0f, 0.45f)] public float edgeWarp = 0.18f;

        [Header("Placement")]
        public bool requireLand = true;
        public Vector2 heightRange = new(0.2f, 1f);
        public Vector2 moistureRange = new(0f, 1f);
        public Vector2 temperatureRange = new(0f, 1f);
        public BiomeData[] allowedBiomes = Array.Empty<BiomeData>();

        [Header("Ordered Recipe")]
        public GeneratorInfo[] generators = Array.Empty<GeneratorInfo>();

        public bool Allows(TerrainGenerationState terrain, float waterHeight)
        {
            if (requireLand && terrain.height <= waterHeight)
                return false;
            if (!InRange(terrain.height, heightRange) ||
                !InRange(terrain.moisture, moistureRange) ||
                !InRange(terrain.temperature, temperatureRange))
            {
                return false;
            }

            if (allowedBiomes == null || allowedBiomes.Length == 0)
                return true;

            BiomeData biome = terrain.biomeData.dominantBiome;
            foreach (BiomeData allowed in allowedBiomes)
            {
                if (allowed == biome)
                    return true;
            }

            return false;
        }

        private static bool InRange(float value, Vector2 range) =>
            value >= Mathf.Min(range.x, range.y) &&
            value <= Mathf.Max(range.x, range.y);

        private void OnValidate()
        {
            persistentId = persistentId?.Trim();
            maximumRadius = Mathf.Max(minimumRadius, maximumRadius);
            minimumAspect = Mathf.Max(0.1f, minimumAspect);
            maximumAspect = Mathf.Max(minimumAspect, maximumAspect);
        }
    }

    [Serializable]
    public sealed class GeneratorInfo
    {
        public bool enabled = true;
        [Min(0f)] public float strength = 1f;

        [SerializeReference]
        [ManagedReferenceSelector(typeof(FeatureGenerator))]
        public FeatureGenerator generator;
    }

    [Serializable]
    public abstract class FeatureGenerator
    {
        public abstract void Generate(
            ref TerrainGenerationState terrain,
            in FeatureGenerationContext context,
            float strength);
    }

    [Serializable]
    public sealed class VolcanoFeatureGenerator : FeatureGenerator
    {
        [Min(0f)] public float coneHeight = 0.44f;
        [Min(0f)] public float rimHeight = 0.18f;
        [Min(0f)] public float craterDepth = 0.32f;
        [Min(0f)] public float ridgeHeight = 0.08f;
        [Min(1)] public int ridgeCount = 11;

        public override void Generate(
            ref TerrainGenerationState terrain,
            in FeatureGenerationContext context,
            float strength)
        {
            float d = context.normalizedDistance;
            if (context.mask <= 0f || d >= 1f || strength <= 0f)
                return;

            float angle = Mathf.Atan2(context.localY, context.localX);
            float ridges = 1f - Mathf.Pow(
                Mathf.Abs(Mathf.Sin(angle * Mathf.Max(1, ridgeCount))),
                5f);
            ridges = Mathf.Clamp01(ridges + context.detailNoise * 0.35f);
            float slopeMask =
                SmoothStep(0.16f, 0.4f, d) *
                (1f - SmoothStep(0.82f, 1f, d));

            float cone =
                Mathf.Pow(1f - Mathf.Clamp01(d), 1.35f) *
                coneHeight;
            float rim = Ring(d, 0.24f, 0.08f) * rimHeight;
            float crater =
                (1f - SmoothStep(0f, 0.42f, d)) *
                craterDepth;
            float ridge = ridges * slopeMask * ridgeHeight;

            terrain.height +=
                (cone + rim + ridge - crater) *
                context.mask *
                strength;
        }

        private static float Ring(float distance, float center, float width)
        {
            float t = Mathf.Abs(distance - center) / Mathf.Max(0.001f, width);
            return 1f - SmoothStep(0f, 1f, t);
        }

        private static float SmoothStep(float minimum, float maximum, float value)
        {
            float t = Mathf.Clamp01(
                (value - minimum) /
                Mathf.Max(0.0001f, maximum - minimum));
            return t * t * (3f - 2f * t);
        }
    }

    public struct TerrainGenerationState
    {
        public float height;
        public float baseHeight;
        public float moisture;
        public float temperature;
        public BiomeBlend biomeData;
    }

    public readonly struct FeatureGenerationContext
    {
        public readonly int worldX;
        public readonly int worldY;
        public readonly Vector2 center;
        public readonly float localX;
        public readonly float localY;
        public readonly float normalizedDistance;
        public readonly float mask;
        public readonly float detailNoise;

        public FeatureGenerationContext(
            int worldX,
            int worldY,
            Vector2 center,
            float localX,
            float localY,
            float normalizedDistance,
            float mask,
            float detailNoise)
        {
            this.worldX = worldX;
            this.worldY = worldY;
            this.center = center;
            this.localX = localX;
            this.localY = localY;
            this.normalizedDistance = normalizedDistance;
            this.mask = mask;
            this.detailNoise = detailNoise;
        }
    }
}
