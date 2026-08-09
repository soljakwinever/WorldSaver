using NUnit.Framework;
using Project.Scripts;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class AreaNameGeneratorTests
    {
        [Test]
        public void Generate_IsStableForWorldBiomeAndRegion()
        {
            BiomeData biome = ScriptableObject.CreateInstance<BiomeData>();
            AreaNameParts parts = ScriptableObject.CreateInstance<AreaNameParts>();
            try
            {
                biome.biomeName = "Rolling Hills";
                biome.areaNameParts = parts;
                parts.style = AreaNameStyle.Compound;
                parts.firstParts = new[] { "Amber", "Wind" };
                parts.middleParts = new[] { "crest", "ward" };
                parts.lastParts = new[] { "reach", "vale" };
                parts.separators = new[] { "", " ", "'" };
                parts.minimumComponents = 2;
                parts.maximumComponents = 3;
                parts.featureTypes = new[] { "Hills", "Slopes" };

                string first = AreaNameGenerator.Generate(
                    1729, biome, new Vector2Int(-3, 8));
                string second = AreaNameGenerator.Generate(
                    1729, biome, new Vector2Int(-3, 8));

                Assert.That(first, Is.EqualTo(second));
                Assert.That(first, Is.Not.Empty);
                Assert.That(first, Does.EndWith("Hills").Or.EndWith("Slopes"));
            }
            finally
            {
                Object.DestroyImmediate(parts);
                Object.DestroyImmediate(biome);
            }
        }

        [Test]
        public void Generate_PresetNameIncludesBiomeFeatureType()
        {
            BiomeData biome = ScriptableObject.CreateInstance<BiomeData>();
            AreaNameParts parts = ScriptableObject.CreateInstance<AreaNameParts>();
            try
            {
                biome.biomeName = "Rolling Hills";
                biome.areaNameParts = parts;
                parts.style = AreaNameStyle.Preset;
                parts.presetNames = new[] { "The Long Downs" };
                parts.featureTypes = new[] { "Slopes" };

                Assert.That(
                    AreaNameGenerator.Generate(42, biome, Vector2Int.zero),
                    Is.EqualTo("The Long Downs Slopes"));
            }
            finally
            {
                Object.DestroyImmediate(parts);
                Object.DestroyImmediate(biome);
            }
        }
    }
}
