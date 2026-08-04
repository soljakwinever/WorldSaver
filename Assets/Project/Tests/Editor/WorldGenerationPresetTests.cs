#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
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
        public void WorldResolvesPlanesByStableId()
        {
            WorldData world = ScriptableObject.CreateInstance<WorldData>();
            PlaneData surface = ScriptableObject.CreateInstance<PlaneData>();
            PlaneData underground = ScriptableObject.CreateInstance<PlaneData>();
            try
            {
                JsonUtility.FromJsonOverwrite(
                    "{\"persistentId\":\"surface\"}", surface);
                JsonUtility.FromJsonOverwrite(
                    "{\"persistentId\":\"underground\"}", underground);
                world.StartPlane = surface;
                world.planes = new[] { surface, underground };

                Assert.That(world.TryGetPlane("underground", out PlaneData found), Is.True);
                Assert.That(found, Is.SameAs(underground));
                Assert.That(world.TryGetPlane("missing", out _), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(surface);
                Object.DestroyImmediate(underground);
                Object.DestroyImmediate(world);
            }
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

        [Test]
        public void ShippedUndergroundEntranceIsPlaceableAndGeneratedOnSurface()
        {
            ItemData item = Resources.Load<ItemData>(
                "Items/Underground Entrance");
            EntityArchetype archetype = Resources.Load<EntityArchetype>(
                "UndergroundEntrance");
            FeatureData feature = Resources.Load<FeatureData>(
                "WorldGeneration/Features/Underground Entrance");
            WorldGenerationPresetData surface =
                Resources.Load<WorldGenerationPresetData>(
                    "WorldGeneration/Standard");
            WorldGenerationPresetData underground =
                Resources.Load<WorldGenerationPresetData>(
                    "WorldGeneration/Underground Caves");

            Assert.That(item, Is.Not.Null);
            Assert.That(
                item.TryGetActionData(
                    out PlacePersistentNodeItemActionData placement),
                Is.True);
            Assert.That(placement.requireWalkableArea, Is.True);
            Assert.That(archetype, Is.Not.Null);
            Assert.That(archetype.NodeData, Is.SameAs(placement.node));
            Assert.That(feature, Is.Not.Null);
            Assert.That(
                feature.generators[1].generator,
                Is.TypeOf<PlaceEntityFeatureGenerator>());
            Assert.That(
                System.Array.IndexOf(surface.features.features, feature),
                Is.GreaterThanOrEqualTo(0));
            Assert.That(
                System.Array.IndexOf(underground.features.features, feature),
                Is.LessThan(0));
        }

        [Test]
        public void ShippedCavePresetOwnsContentAndProducesAllTerrainKinds()
        {
            WorldGenerationPresetData cave =
                Resources.Load<WorldGenerationPresetData>(
                    "WorldGeneration/Underground Caves");
            Assert.That(cave, Is.Not.Null);
            Assert.That(cave.IsComplete, Is.True);
            Assert.That(cave.caveLayout, Is.Not.Null);
            Assert.That(cave.caveBiomeMap, Is.Not.Null);
            Assert.That(cave.climate.useLegacyResourceBiomes, Is.False);
            Assert.That(cave.climate.biomes, Has.Length.GreaterThan(1));
            Assert.That(cave.surfaceDetails.useLegacyWorldPropRules, Is.False);

            WorldData world = ScriptableObject.CreateInstance<WorldData>();
            world.seed = 24680;
            try
            {
                WorldGeneration generator = new(
                    world,
                    new WorldGenerationSelection(world.seed, cave),
                    null);
                bool floor = false;
                bool wall = false;
                bool crevasse = false;
                for (int y = -256; y <= 256; y += 4)
                for (int x = -256; x <= 256; x += 4)
                {
                    TerrainKind first = generator.GetTerrainKind(x, y);
                    Assert.That(generator.GetTerrainKind(x, y), Is.EqualTo(first));
                    floor |= first == TerrainKind.Floor;
                    wall |= first == TerrainKind.Wall;
                    crevasse |= first == TerrainKind.Crevasse;
                }

                Assert.That(floor, Is.True);
                Assert.That(wall, Is.True);
                Assert.That(crevasse, Is.True);

                HashSet<BiomeData> encounteredBiomes = new();
                for (int y = -1024; y <= 1024; y += 128)
                for (int x = -1024; x <= 1024; x += 128)
                {
                    TerrainSample first = generator.GetTerrainSample(x, y);
                    TerrainSample second = generator.GetTerrainSample(x, y);
                    Assert.That(second.biome, Is.SameAs(first.biome));
                    encounteredBiomes.Add(first.biome);
                }
                Assert.That(encounteredBiomes.Count, Is.GreaterThanOrEqualTo(4));

                generator.GetTileForChunk(
                    37,
                    -19,
                    out _,
                    out float chunkHeight,
                    out _,
                    out _,
                    out _,
                    out TerrainKind chunkKind,
                    out ChunkBuildResult.IsCliff chunkCliff);
                TerrainSample directSample =
                    generator.GetTerrainSample(37, -19);
                Assert.That(chunkKind, Is.EqualTo(directSample.terrainKind));
                Assert.That(chunkHeight, Is.EqualTo(directSample.height));
                Assert.That(chunkCliff.cliff,
                    Is.EqualTo(chunkKind == TerrainKind.Wall));

                Assert.That(
                    generator.TryFindSafePortalPosition(
                        Vector2Int.zero,
                        out Vector2Int portal,
                        searchRadius: 128,
                        clearanceRadius: 2),
                    Is.True);
                for (int y = -2; y <= 2; y++)
                for (int x = -2; x <= 2; x++)
                {
                    Assert.That(
                        generator.GetTerrainSample(
                            portal.x + x,
                            portal.y + y).IsWalkable,
                        Is.True);
                }
            }
            finally
            {
                Object.DestroyImmediate(world);
            }
        }
    }
}
#endif
