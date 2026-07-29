#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Project.Scripts;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class WorldGenerationPresetTests
    {
        [Test]
        public void VolcanoGeneratorLeavesBiomeAndClimateUntouched()
        {
            BiomeData biome = ScriptableObject.CreateInstance<BiomeData>();
            TerrainGenerationState terrain = new()
            {
                height = 0.4f,
                moisture = 0.35f,
                temperature = 0.7f,
                biomeData = new BiomeBlend { dominantBiome = biome }
            };
            FeatureGenerationContext context = new(
                10,
                20,
                Vector2.zero,
                5f,
                4f,
                0.5f,
                1f,
                0.1f);

            new VolcanoFeatureGenerator().Generate(
                ref terrain,
                in context,
                1f);

            Assert.That(terrain.height, Is.Not.EqualTo(0.4f));
            Assert.That(terrain.moisture, Is.EqualTo(0.35f));
            Assert.That(terrain.temperature, Is.EqualTo(0.7f));
            Assert.That(terrain.biomeData.dominantBiome, Is.SameAs(biome));
            Object.DestroyImmediate(biome);
        }

        [Test]
        public void VolcanoGeneratorDoesNothingOutsideFootprint()
        {
            TerrainGenerationState terrain = new() { height = 0.4f };
            FeatureGenerationContext context = new(
                0,
                0,
                Vector2.zero,
                100f,
                100f,
                1.2f,
                0f,
                0f);

            new VolcanoFeatureGenerator().Generate(
                ref terrain,
                in context,
                1f);

            Assert.That(terrain.height, Is.EqualTo(0.4f));
        }

        [Test]
        public void CatalogResolvesExactIdAndVersion()
        {
            WorldGenerationPresetData preset =
                ScriptableObject.CreateInstance<WorldGenerationPresetData>();
            WorldGenerationPresetCatalogData catalog =
                ScriptableObject.CreateInstance<WorldGenerationPresetCatalogData>();
            catalog.presets = new[] { preset };

            Assert.That(
                catalog.TryResolve(
                    preset.PersistentId,
                    preset.Version,
                    out WorldGenerationPresetData resolved),
                Is.True);
            Assert.That(resolved, Is.SameAs(preset));
            Assert.That(
                catalog.TryResolve(
                    preset.PersistentId,
                    preset.Version + 1,
                    out _),
                Is.False);

            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(preset);
        }

        [Test]
        public void ShippedCatalogAndVolcanoRecipeLoadFromResources()
        {
            WorldGenerationPresetCatalogData catalog =
                Resources.Load<WorldGenerationPresetCatalogData>(
                    "WorldGeneration/PresetCatalog");

            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.newWorldDefault, Is.Not.Null);
            Assert.That(catalog.newWorldDefault.IsComplete, Is.True);
            Assert.That(catalog.legacyFallback, Is.Not.Null);
            Assert.That(catalog.legacyFallback.IsComplete, Is.True);

            FeatureData volcano =
                catalog.newWorldDefault.features.features[0];
            Assert.That(volcano, Is.Not.Null);
            Assert.That(volcano.generators, Has.Length.EqualTo(1));
            Assert.That(
                volcano.generators[0].generator,
                Is.TypeOf<VolcanoFeatureGenerator>());
        }
    }
}
#endif
