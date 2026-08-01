using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "Feature", menuName = "World Generation/Feature")]
    public class FeatureData : ScriptableObject
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

        protected virtual void OnValidate()
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

    [Serializable]
    public sealed class FoundationFeatureGenerator : FeatureGenerator
    {
        [Min(0f)] public float platformHeight = 0.04f;
        [Range(0f, 1f)] public float flattenStrength = 0.8f;

        public override void Generate(
            ref TerrainGenerationState terrain,
            in FeatureGenerationContext context,
            float strength)
        {
            if (context.mask <= 0f || strength <= 0f)
                return;

            float blend = Mathf.Clamp01(
                context.mask * flattenStrength * strength);
            float foundation = terrain.baseHeight +
                               platformHeight *
                               context.mask *
                               strength;
            terrain.height = Mathf.Lerp(
                terrain.height,
                foundation,
                blend);
        }
    }

    [Serializable]
    public abstract class FloorGenerator : FeatureGenerator
    {
        public TileData tile;
        public Vector2 offset;
        public abstract Vector2 HalfExtents { get; }

        public sealed override void Generate(
            ref TerrainGenerationState terrain,
            in FeatureGenerationContext context,
            float strength)
        {
            if (tile == null || strength <= 0f || context.mask <= 0f)
                return;

            if (TryGetTile(
                    new Vector2(context.localX, context.localY),
                    out TileData generatedTile))
            {
                terrain.floorTile = generatedTile;
            }
        }

        public bool TryGetTile(Vector2 featureLocalPoint, out TileData result)
        {
            result = tile;
            return result != null && Contains(featureLocalPoint - offset);
        }

        protected abstract bool Contains(Vector2 point);
    }

    [Serializable]
    public sealed class CrossFloorGenerator : FloorGenerator
    {
        [Tooltip("Draw diagonals (X) instead of horizontal and vertical arms (+).")]
        public bool diagonal;
        [Min(1f)] public float size = 9f;
        [Min(1f)] public float width = 1f;
        public override Vector2 HalfExtents =>
            Vector2.one * Mathf.Max(0.5f, size * 0.5f);

        protected override bool Contains(Vector2 point)
        {
            float halfSize = Mathf.Max(0.5f, size * 0.5f);
            float halfWidth = Mathf.Max(0.5f, width * 0.5f);
            if (Mathf.Abs(point.x) > halfSize || Mathf.Abs(point.y) > halfSize)
                return false;

            return diagonal
                ? Mathf.Min(
                    Mathf.Abs(point.y - point.x),
                    Mathf.Abs(point.y + point.x)) <= halfWidth * Mathf.Sqrt(2f)
                : Mathf.Abs(point.x) <= halfWidth ||
                  Mathf.Abs(point.y) <= halfWidth;
        }
    }

    [Serializable]
    public sealed class RectangleFloorGenerator : FloorGenerator
    {
        [Min(1f)] public float width = 9f;
        [Min(1f)] public float height = 9f;
        public override Vector2 HalfExtents => new(
            Mathf.Max(0.5f, width * 0.5f),
            Mathf.Max(0.5f, height * 0.5f));

        protected override bool Contains(Vector2 point) =>
            Mathf.Abs(point.x) <= Mathf.Max(0.5f, width * 0.5f) &&
            Mathf.Abs(point.y) <= Mathf.Max(0.5f, height * 0.5f);
    }

    [Serializable]
    public sealed class OutlineRectangleFloorGenerator : FloorGenerator
    {
        [Min(1f)] public float width = 9f;
        [Min(1f)] public float height = 9f;
        [Min(1f)] public float borderWidth = 1f;
        [Min(0f), Tooltip("Radius of the rectangle's rounded corners.")]
        public float borderRadius;
        public override Vector2 HalfExtents => new(
            Mathf.Max(0.5f, width * 0.5f),
            Mathf.Max(0.5f, height * 0.5f));

        protected override bool Contains(Vector2 point)
        {
            float halfWidth = Mathf.Max(0.5f, width * 0.5f);
            float halfHeight = Mathf.Max(0.5f, height * 0.5f);
            float radius = Mathf.Clamp(
                borderRadius,
                0f,
                Mathf.Min(halfWidth, halfHeight));
            if (RoundedRectangleDistance(
                    point,
                    new Vector2(halfWidth, halfHeight),
                    radius) > 0f)
            {
                return false;
            }

            float thickness = Mathf.Max(1f, borderWidth);
            Vector2 innerHalfSize = new(
                halfWidth - thickness,
                halfHeight - thickness);
            if (innerHalfSize.x <= 0f || innerHalfSize.y <= 0f)
                return true;

            float innerRadius = Mathf.Max(0f, radius - thickness);
            return RoundedRectangleDistance(point, innerHalfSize, innerRadius) >= 0f;
        }

        private static float RoundedRectangleDistance(
            Vector2 point,
            Vector2 halfSize,
            float radius)
        {
            Vector2 q = new(
                Mathf.Abs(point.x) - halfSize.x + radius,
                Mathf.Abs(point.y) - halfSize.y + radius);
            Vector2 outside = new(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
            return outside.magnitude +
                   Mathf.Min(Mathf.Max(q.x, q.y), 0f) -
                   radius;
        }
    }

    [Serializable]
    public sealed class CircleFloorGenerator : FloorGenerator
    {
        [Min(1f)] public float diameter = 9f;
        public override Vector2 HalfExtents =>
            Vector2.one * Mathf.Max(0.5f, diameter * 0.5f);

        protected override bool Contains(Vector2 point) =>
            point.sqrMagnitude <=
            Mathf.Pow(Mathf.Max(0.5f, diameter * 0.5f), 2f);
    }

    [Serializable]
    public sealed class OutlineCircleFloorGenerator : FloorGenerator
    {
        [Min(1f)] public float diameter = 9f;
        [Min(1f)] public float borderWidth = 1f;
        public override Vector2 HalfExtents =>
            Vector2.one * Mathf.Max(0.5f, diameter * 0.5f);

        protected override bool Contains(Vector2 point)
        {
            float radius = Mathf.Max(0.5f, diameter * 0.5f);
            float innerRadius = Mathf.Max(0f, radius - Mathf.Max(1f, borderWidth));
            float distanceSquared = point.sqrMagnitude;
            return distanceSquared <= radius * radius &&
                   distanceSquared >= innerRadius * innerRadius;
        }
    }

    [Serializable]
    public sealed class StarFloorGenerator : FloorGenerator
    {
        [Min(2f)] public float width = 11f;
        [Min(2f)] public float height = 11f;
        [Min(2)] public int points = 5;
        [Range(0.05f, 0.95f)]
        public float innerRadius = 0.45f;
        public override Vector2 HalfExtents => new(
            Mathf.Max(1f, width * 0.5f),
            Mathf.Max(1f, height * 0.5f));

        protected override bool Contains(Vector2 point)
        {
            float halfWidth = Mathf.Max(1f, width * 0.5f);
            float halfHeight = Mathf.Max(1f, height * 0.5f);
            Vector2 normalized = new(point.x / halfWidth, point.y / halfHeight);
            int pointCount = Mathf.Max(2, points);
            int vertexCount = pointCount * 2;
            bool inside = false;
            Vector2 previous = Vertex(vertexCount - 1, vertexCount);

            for (int i = 0; i < vertexCount; i++)
            {
                Vector2 current = Vertex(i, vertexCount);
                bool crosses =
                    (current.y > normalized.y) != (previous.y > normalized.y) &&
                    normalized.x <
                    (previous.x - current.x) *
                    (normalized.y - current.y) /
                    (previous.y - current.y) +
                    current.x;
                if (crosses)
                    inside = !inside;
                previous = current;
            }

            return inside;
        }

        private Vector2 Vertex(int index, int vertexCount)
        {
            float angle = -Mathf.PI * 0.5f +
                          index * Mathf.PI * 2f / vertexCount;
            float radius = index % 2 == 0
                ? 1f
                : Mathf.Clamp(innerRadius, 0.05f, 0.95f);
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
    }

    public struct TerrainGenerationState
    {
        public float height;
        public float baseHeight;
        public float moisture;
        public float temperature;
        public BiomeBlend biomeData;
        public TileData floorTile;
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
