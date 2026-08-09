using System;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public static class AreaNameGenerator
    {
        public static string Generate(
            int worldSeed,
            BiomeData biome,
            Vector2Int regionCoordinate)
        {
            AreaNameParts parts = biome != null ? biome.areaNameParts : null;
            if (parts == null)
                return string.Empty;

            uint state = StableSeed(worldSeed, biome, regionCoordinate);
            string properName = parts.style == AreaNameStyle.Preset
                ? Pick(parts.presetNames, ref state)
                : BuildCompound(parts, ref state);
            string feature = Pick(parts.featureTypes, ref state);

            if (string.IsNullOrWhiteSpace(properName))
                return feature?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(feature))
                return properName.Trim();
            return $"{properName.Trim()} {feature.Trim()}";
        }

        private static string BuildCompound(AreaNameParts parts, ref uint state)
        {
            int minimum = Mathf.Clamp(parts.minimumComponents, 1, 3);
            int maximum = Mathf.Clamp(parts.maximumComponents, minimum, 3);
            int count = minimum + Next(ref state, maximum - minimum + 1);
            string first = Pick(parts.firstParts, ref state);
            string middle = count >= 3 ? Pick(parts.middleParts, ref state) : null;
            string last = count >= 2 ? Pick(parts.lastParts, ref state) : null;

            string result = first?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(middle))
                result = Join(result, middle.Trim(), parts.separators, ref state);
            if (!string.IsNullOrWhiteSpace(last))
                result = Join(result, last.Trim(), parts.separators, ref state);
            return result;
        }

        private static string Join(
            string left,
            string right,
            string[] separators,
            ref uint state)
        {
            if (string.IsNullOrEmpty(left))
                return right;
            string separator = Pick(separators, ref state) ?? string.Empty;
            return left + separator + right;
        }

        private static string Pick(string[] values, ref uint state)
        {
            if (values == null || values.Length == 0)
                return string.Empty;
            return values[Next(ref state, values.Length)] ?? string.Empty;
        }

        private static int Next(ref uint state, int upperBound)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return upperBound <= 1 ? 0 : (int)(state % (uint)upperBound);
        }

        private static uint StableSeed(
            int worldSeed,
            BiomeData biome,
            Vector2Int coordinate)
        {
            uint hash = 2166136261u;
            Add(ref hash, unchecked((uint)worldSeed));
            Add(ref hash, unchecked((uint)coordinate.x));
            Add(ref hash, unchecked((uint)coordinate.y));
            string id = biome != null && !string.IsNullOrWhiteSpace(biome.biomeName)
                ? biome.biomeName
                : biome != null ? biome.name : string.Empty;
            foreach (char character in id ?? string.Empty)
                Add(ref hash, char.ToUpperInvariant(character));
            return hash == 0 ? 0x9E3779B9u : hash;
        }

        private static void Add(ref uint hash, uint value)
        {
            hash ^= value;
            hash *= 16777619u;
        }
    }
}
