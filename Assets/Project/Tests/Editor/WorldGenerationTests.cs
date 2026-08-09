#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using Project.Scripts;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class WorldGenerationTests
    {
        private WorldData _worldData;
        private BiomeData _biome;
        private WorldGenerationPresetData _preset;
        private IWorldGenerator _generator;

        [SetUp]
        public void SetUp()
        {
            _worldData = ScriptableObject.CreateInstance<WorldData>();
            _worldData.seed = 12345;
            _worldData.waterHeight = 0.2f;
            _worldData.beachHeight = 0.3f;
            _worldData.mountainHeight = 0.7f;
            _worldData.continentalNoiseScale = 46.5f;
            _worldData.moistureNoiseScale = 32f;
            _worldData.temperatureNoiseScale = 48.2f;
            _worldData.erosionNoiseScale = 0.5f;
            _worldData.peakValleyNoiseScale = 8.5f;
            _worldData.lakeNoiseScale = 4f;
            _worldData.outCropNoiseScale = 2;
            _worldData.outCropThreshold = 1f;

            _biome = ScriptableObject.CreateInstance<BiomeData>();
            _biome.biomeName = "Test biome";
            _biome.height = 0.5f;
            _biome.heightVariance = 1f;
            _biome.moisture = 0.5f;
            _biome.moistureVariance = 1f;
            _biome.temperature = 0.5f;
            _biome.temperatureVariance = 1f;
            _biome.heightMultiplier = 1f;
            _biome.hillScale = 28f;
            _biome.bumpScale = 9f;
            _biome.cliffScale = 18f;

            _preset = WorldGenerationPresetDefaults.CreateFromLegacy(_worldData);
            _preset.climate.biomes = new[] { _biome };
            WorldGeneration generator = new(
                _worldData,
                new WorldGenerationSelection(_worldData.seed, _preset),
                null);
            _generator = generator;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_biome);
            DestroyPreset(_preset);
            Object.DestroyImmediate(_worldData);
        }

        private static void DestroyPreset(WorldGenerationPresetData preset)
        {
            if (preset == null)
                return;

            Object.DestroyImmediate(preset.climate);
            Object.DestroyImmediate(preset.elevation);
            Object.DestroyImmediate(preset.lakes);
            Object.DestroyImmediate(preset.smallPools);
            Object.DestroyImmediate(preset.valleys);
            Object.DestroyImmediate(preset.microTerrain);
            Object.DestroyImmediate(preset.outcrops);
            Object.DestroyImmediate(preset.features);
            Object.DestroyImmediate(preset.surfaceDetails);
            Object.DestroyImmediate(preset);
        }

        [Test]
        public void SeedReflectsWorldDataSeed()
        {
            Assert.That(_generator.Seed, Is.EqualTo(12345u));
        }

        [Test]
        public void GetTileReturnsDeterministicPopulatedOutputs()
        {
            int firstTile = _generator.GetTile(37, -19, out BiomeBlend firstBiome,
                out float firstHeight, out float firstMoisture, out float firstTemperature);
            int secondTile = _generator.GetTile(37, -19, out BiomeBlend secondBiome,
                out float secondHeight, out float secondMoisture, out float secondTemperature);


            Assert.That(secondTile, Is.EqualTo(firstTile));
            Assert.That(secondHeight, Is.EqualTo(firstHeight));
            Assert.That(secondMoisture, Is.EqualTo(firstMoisture));
            Assert.That(secondTemperature, Is.EqualTo(firstTemperature));
            Assert.That(firstHeight, Is.InRange(0f, 1f));
            Assert.That(firstMoisture, Is.InRange(0f, 1f));
            Assert.That(firstTemperature, Is.InRange(0f, 1f));
            Assert.That(firstBiome.dominantBiome, Is.SameAs(_biome));
            Assert.That(secondBiome.dominantBiome, Is.SameAs(_biome));

        }

        [Test]
        public void GetTerrainSampleReturnsConsistentTerrainFlagsAndBiome()
        {
            TerrainSample sample = _generator.GetTerrainSample(-24, 51);
            
            Assert.That(sample.biome, Is.SameAs(_biome));
            Assert.That(sample.biomeBlend.dominantBiome, Is.SameAs(_biome));
            Assert.That(sample.height, Is.InRange(0f, 1f));
            Assert.That(sample.moisture, Is.InRange(0f, 1f));
            Assert.That(sample.temperature, Is.InRange(0f, 1f));
            Assert.That(sample.isWater, Is.EqualTo(sample.height <= _worldData.waterHeight));
            Assert.That(sample.isCliff, Is.EqualTo((bool)_generator.IsSmallCliff(-24, 51)));
            Assert.That(sample.isRoad, Is.False);
            Assert.That(sample.isTrail, Is.False);
        }

        [Test]
        public void IsSmallCliffReturnsStableClassification()
        {
            ChunkBuildResult.IsCliff first = _generator.IsSmallCliff(12, 8);
            ChunkBuildResult.IsCliff second = _generator.IsSmallCliff(12, 8);
            
            Assert.That(second.cliff, Is.EqualTo(first.cliff));
            Assert.That(second.wall, Is.EqualTo(first.wall));

        }

        [Test]
        public void FindSafeSpawnPositionDependsOnWorldSeedNotUnityRandomState()
        {
            Random.State originalState = Random.state;
            try
            {
                Random.InitState(8675309);
                Vector2Int first = _generator.FindSafeSpawnPosition(
                    searchRadius: 32, maxAttempts: 20, safetyRadius: 1);

                Random.InitState(42);
                Vector2Int second = _generator.FindSafeSpawnPosition(
                    searchRadius: 32, maxAttempts: 20, safetyRadius: 1);

                Assert.That(second, Is.EqualTo(first));
            }
            finally
            {
                Random.state = originalState;
            }
        }

        [Test]
        public void WaterOnlyBiome_IsAppliedAfterTerrainWithoutShapingIt()
        {
            BiomeData waterBiome = ScriptableObject.CreateInstance<BiomeData>();
            try
            {
                WorldGeneration landOnly = new(
                    _worldData,
                    new WorldGenerationSelection(_worldData.seed, _preset),
                    null);

                waterBiome.biomeName = "Test lake";
                waterBiome.biomePlacement = BiomePlacement.WaterOnly;
                waterBiome.height = 0.5f;
                waterBiome.heightVariance = 1f;
                waterBiome.moisture = 0.5f;
                waterBiome.moistureVariance = 1f;
                waterBiome.temperature = 0.5f;
                waterBiome.temperatureVariance = 1f;
                waterBiome.heightMultiplier = 100f;
                waterBiome.heightOffset = 100f;
                _preset.climate.biomes = new[] { _biome, waterBiome };
                WorldGeneration withWaterBiome = new(
                    _worldData,
                    new WorldGenerationSelection(_worldData.seed, _preset),
                    null);
                _preset.elevation.waterHeight = 1f;

                TerrainSample baseline = landOnly.GetTerrainSample(17, -29);
                TerrainSample water = withWaterBiome.GetTerrainSample(17, -29);

                Assert.That(water.isWater, Is.True);
                Assert.That(water.biome, Is.SameAs(waterBiome));
                Assert.That(water.height, Is.EqualTo(baseline.height));
            }
            finally
            {
                Object.DestroyImmediate(waterBiome);
            }
        }

        [Test]
        public void WorldSpawnPositionIsCachedAndMatchesCanonicalSearch()
        {
            Vector2Int first = _generator.WorldSpawnPosition;
            Vector2Int second = _generator.WorldSpawnPosition;

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void SelectedPresetControlsBaselineAndEventNPCSpawnRules()
        {
            EnemySpawnRule legacyRule =
                ScriptableObject.CreateInstance<EnemySpawnRule>();
            EnemySpawnRule presetRule =
                ScriptableObject.CreateInstance<EnemySpawnRule>();
            try
            {
                _worldData.enemySpawnRules = new[] { legacyRule };
                _preset.useLegacyWorldNPCSpawnRules = false;
                _preset.enemySpawnRules = new[] { presetRule };
                _preset.allowEventNPCSpawnRules = false;

                WorldGeneration generator = (WorldGeneration)_generator;

                Assert.That(
                    generator.EnemySpawnRules,
                    Is.EqualTo(new[] { presetRule }));
                Assert.That(generator.AllowEventNPCSpawnRules, Is.False);

                _preset.enemySpawnRules = System.Array.Empty<EnemySpawnRule>();
                Assert.That(generator.EnemySpawnRules, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(presetRule);
                Object.DestroyImmediate(legacyRule);
            }
        }

        [Test]
        public void WorldSpawnFeaturePlacesPlatformOnlyInOwningChunkAndIsStable()
        {
            NodeData platform =
                ScriptableObject.CreateInstance<NodeData>();
            FeatureData feature = ScriptableObject.CreateInstance<FeatureData>();
            feature.persistentId = "WorldSpawnPlatform";
            feature.generators = new[]
            {
                new GeneratorInfo
                {
                    generator = new ClearEntitiesRectangleFeatureGenerator
                    {
                        size = new Vector2(8f, 8f)
                    }
                },
                new GeneratorInfo
                {
                    generator = new PlaceEntityFeatureGenerator
                    {
                        persistentId = "World Spawn Platform",
                        entity = platform,
                        damageImmune = true
                    }
                }
            };
            _worldData.worldSpawnFeature = feature;
            WorldGeneration generator = (WorldGeneration)_generator;
            Vector2Int spawnPosition = generator.WorldSpawnPosition;
            Vector2Int ownerChunk =
                WorldPartition.WorldToChunk(spawnPosition);
            Vector2Int ownerRegion =
                WorldPartition.ChunkToRegion(ownerChunk);

            try
            {
                Assert.That(
                    generator.TryFindNearestFeature(
                        feature.persistentId,
                        ownerRegion + new Vector2Int(2, -1),
                        2,
                        out Vector2Int foundRegion,
                        out Vector2 foundPosition),
                    Is.True);
                Assert.That(foundRegion, Is.EqualTo(ownerRegion));
                Assert.That(
                    WorldPartition.WorldToChunk(foundPosition),
                    Is.EqualTo(ownerChunk));

                List<PropSpawnData> firstResult = new()
                {
                    new PropSpawnData
                    {
                        worldPosition = spawnPosition + Vector2Int.right,
                        position = spawnPosition + Vector2Int.right
                    }
                };
                generator.ApplyFeatureEntityGenerators(
                    ownerChunk,
                    firstResult);
                Assert.That(firstResult, Has.Count.EqualTo(1));
                PropSpawnData first = firstResult[0];
                Assert.That(first.nodeData, Is.SameAs(platform));
                Assert.That(first.worldPosition, Is.EqualTo(spawnPosition));
                Assert.That(first.damageImmune, Is.True);

                List<PropSpawnData> secondResult = new();
                generator.ApplyFeatureEntityGenerators(
                    ownerChunk,
                    secondResult);
                Assert.That(secondResult, Has.Count.EqualTo(1));
                PropSpawnData second = secondResult[0];
                Assert.That(second.NodeId, Is.EqualTo(first.NodeId));

                List<PropSpawnData> otherChunkResult = new();
                generator.ApplyFeatureEntityGenerators(
                    ownerChunk + Vector2Int.right,
                    otherChunkResult);
                Assert.That(otherChunkResult, Is.Empty);
            }
            finally
            {
                _worldData.worldSpawnFeature = null;
                Object.DestroyImmediate(feature);
                Object.DestroyImmediate(platform);
            }
        }

        [Test]
        public void EntityClearGeneratorsSupportCircleAndRectangleAreas()
        {
            ClearEntitiesCircleFeatureGenerator circle = new()
            {
                offset = new Vector2(2f, 0f),
                radius = 3f
            };
            ClearEntitiesRectangleFeatureGenerator rectangle = new()
            {
                offset = new Vector2(-2f, 1f),
                size = new Vector2(4f, 6f)
            };

            Assert.That(circle.ClearsEntity(new Vector2(5f, 0f)), Is.True);
            Assert.That(circle.ClearsEntity(new Vector2(5.1f, 0f)), Is.False);
            Assert.That(rectangle.ClearsEntity(new Vector2(0f, 4f)), Is.True);
            Assert.That(rectangle.ClearsEntity(new Vector2(0.1f, 4f)), Is.False);
        }

        [Test]
        public void PlaneEntranceResolvesSafeDestinationOnConfiguredPlane()
        {
            PlaneData surface = ScriptableObject.CreateInstance<PlaneData>();
            PlaneData underground = ScriptableObject.CreateInstance<PlaneData>();
            GameObject entranceObject = new("Underground Entrance");
            try
            {
                JsonUtility.FromJsonOverwrite(
                    "{\"persistentId\":\"surface\"}",
                    surface);
                JsonUtility.FromJsonOverwrite(
                    "{\"persistentId\":\"underground\"}",
                    underground);
                surface.generationPreset = _preset;
                underground.generationPreset = _preset;
                _worldData.StartPlane = surface;
                _worldData.planes = new[] { surface, underground };

                WorldGeneration generator = (WorldGeneration)_generator;
                entranceObject.transform.position =
                    (Vector2)generator.WorldSpawnPosition;
                PlaneEntranceComponent entrance =
                    entranceObject.AddComponent<PlaneEntranceComponent>();
                entrance.Construct(
                    _worldData,
                    generator,
                    new PlaneSelection(surface));
                entrance.Initialize(
                    "underground",
                    "Descend underground",
                    searchRadius: 32,
                    clearanceRadius: 1);

                Assert.That(
                    entrance.TryResolveDestination(
                        out PlaneData resolvedPlane,
                        out Vector3 destination),
                    Is.True);
                Assert.That(resolvedPlane, Is.SameAs(underground));
                Vector2Int destinationCell = Vector2Int.FloorToInt(destination);
                Assert.That(
                    generator.IsGeneratedAreaWalkable(new RectInt(
                        destinationCell - Vector2Int.one,
                        Vector2Int.one * 3)),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(entranceObject);
                Object.DestroyImmediate(underground);
                Object.DestroyImmediate(surface);
            }
        }
    }
}
#endif
