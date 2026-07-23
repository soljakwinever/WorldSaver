#if UNITY_INCLUDE_TESTS
using System.Reflection;
using NUnit.Framework;
using Project.Scripts;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class WorldGenerationTests
    {
        private WorldData _worldData;
        private BiomeData _biome;
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

            WorldGeneration generator = new(_worldData);
            FieldInfo biomeLibraryField = typeof(WorldGeneration)
                .GetField("biomeLibrary", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(biomeLibraryField, Is.Not.Null);
            biomeLibraryField.SetValue(generator, new[] { _biome });
            _generator = generator;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_biome);
            Object.DestroyImmediate(_worldData);
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
        public void FindSafeSpawnPositionIsDeterministicForFixedRandomState()
        {
            Random.State originalState = Random.state;
            try
            {
                Random.InitState(8675309);
                Vector2Int first = _generator.FindSafeSpawnPosition(
                    searchRadius: 32, maxAttempts: 20, safetyRadius: 1);

                Random.InitState(8675309);
                Vector2Int second = _generator.FindSafeSpawnPosition(
                    searchRadius: 32, maxAttempts: 20, safetyRadius: 1);

                Assert.That(second, Is.EqualTo(first));
            }
            finally
            {
                Random.state = originalState;
            }
        }
    }
}
#endif
