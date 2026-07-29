using System;
using System.Collections.Generic;
using NUnit.Framework;
using Project.Scripts;
using Project.Scripts.DataTypes;
using Project.Scripts.Enums;
using Project.Scripts.Interface;
using Project.Scripts.TimeAndWeather;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Project.Tests.EditMode
{
    public sealed class RegionalClimateServiceTests
    {
        private WeatherSimulationSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<WeatherSimulationSettings>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_settings);
        }

        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(false, false, false)]
        [TestCase(false, true, false)]
        public void FullScreenWeatherIsMaskedWhileViewerIsIndoors(
            bool isCameraRegion,
            bool isViewerIndoors,
            bool expected)
        {
            Assert.That(
                WeatherEffectPresenter.ShouldPresentFullScreenEffect(
                    isCameraRegion,
                    isViewerIndoors),
                Is.EqualTo(expected));
        }

        [Test]
        public void DefaultSamplingUsesFourQuarterCentersPerChunk()
        {
            RecordingWorldGenerator generator = new();
            RegionalClimateService service = CreateService(generator);

            RegionalClimateBaseline climate =
                service.GetBaseline(new Vector2Int(-1, -1));

            Assert.That(climate.SampleCount, Is.EqualTo(8 * 8 * 4));
            Assert.That(generator.Positions.Count, Is.EqualTo(8 * 8 * 4));
            Assert.That(generator.Positions, Does.Contain(new Vector2Int(-248, -248)));
            Assert.That(generator.Positions, Does.Contain(new Vector2Int(-8, -8)));
        }

        [Test]
        public void BaselineIsCachedByRegion()
        {
            RecordingWorldGenerator generator = new();
            RegionalClimateService service = CreateService(generator);
            Vector2Int region = new(2, -3);

            RegionalClimateBaseline first = service.GetBaseline(region);
            RegionalClimateBaseline second = service.GetBaseline(region);

            Assert.That(second, Is.SameAs(first));
            Assert.That(generator.Positions.Count, Is.EqualTo(8 * 8 * 4));
        }

        [Test]
        public void SnapshotIsBoundedAndDeterministic()
        {
            RecordingWorldGenerator generator = new();
            RegionalClimateService service = CreateService(generator);
            ClimateContext context =
                new(Season.Winter, 12, 0.25f);

            ClimateSnapshot first =
                service.GetSnapshot(Vector2Int.zero, context);
            ClimateSnapshot second =
                service.GetSnapshot(Vector2Int.zero, context);

            Assert.That(second.Temperature, Is.EqualTo(first.Temperature));
            Assert.That(second.Moisture, Is.EqualTo(first.Moisture));
            Assert.That(first.Temperature, Is.InRange(-1f, 1f));
            Assert.That(first.Moisture, Is.InRange(0f, 1f));
        }

        [Test]
        public void LocalTemperatureVariesContinuouslyAcrossChunkBoundary()
        {
            RecordingWorldGenerator generator = new()
            {
                TemperatureAt = (x, _) => x switch
                {
                    31 => 0.49f,
                    32 => 0.51f,
                    _ => 0.5f
                }
            };
            RegionalClimateService service = CreateService(generator);
            ClimateSnapshot regional = service.GetCurrentSnapshot(Vector2Int.zero);

            ClimateSnapshot left = service.GetLocalSnapshot(
                new Vector2(31.5f, 10.5f),
                regional);
            ClimateSnapshot right = service.GetLocalSnapshot(
                new Vector2(32.5f, 10.5f),
                regional);

            Assert.That(right.Temperature - left.Temperature,
                Is.EqualTo(0.04f).Within(0.0001f));
        }

        [Test]
        public void RegionSimulationResultRequiresMatchingRevision()
        {
            RuntimeRegion live = new(Vector2Int.zero, 10);
            live.SetComponent(new RegionComponentRecord
            {
                typeId = 1,
                data = new byte[] { 1 }
            });
            ulong expectedRevision = live.Revision;

            RuntimeRegion detached =
                RegionSaveData.CreateSnapshot(live).CreateRuntimeRegion();
            detached.SetComponent(new RegionComponentRecord
            {
                typeId = 1,
                data = new byte[] { 2 }
            });
            detached.LastSimulatedTick = 20;

            Assert.That(
                live.TryApplySimulationResult(detached, expectedRevision),
                Is.True);
            Assert.That(live.LastSimulatedTick, Is.EqualTo(20));
            Assert.That(live.Components[1].data[0], Is.EqualTo(2));

            RuntimeRegion stale =
                RegionSaveData.CreateSnapshot(live).CreateRuntimeRegion();
            ulong staleRevision = live.Revision;
            live.MarkDirty();

            Assert.That(
                live.TryApplySimulationResult(stale, staleRevision),
                Is.False);
        }

        private RegionalClimateService CreateService(
            RecordingWorldGenerator generator)
        {
            return new RegionalClimateService(
                generator,
                new FixedTimeController(),
                new NullWeatherModifierSource(),
                _settings);
        }

        private sealed class RecordingWorldGenerator : IWorldGenerator
        {
            public readonly List<Vector2Int> Positions = new();
            public Func<int, int, float> TemperatureAt =
                (_, _) => 0.45f;
            public uint Seed => 1234;

            public int GetTile(
                int x,
                int y,
                out BiomeBlend biomeData,
                out float height,
                out float moisture,
                out float temperature)
            {
                throw new NotSupportedException();
            }

            public TerrainSample GetTerrainSample(int x, int y)
            {
                Positions.Add(new Vector2Int(x, y));
                return new TerrainSample
                {
                    height = 0.4f,
                    moisture = 0.6f,
                    temperature = TemperatureAt(x, y),
                    isWater = (x + y) % 3 == 0,
                    isCliff = (x + y) % 5 == 0
                };
            }

            public Vector2Int FindSafeSpawnPosition(
                int searchRadius = 512,
                int maxAttempts = 5000,
                int safetyRadius = 3,
                float minHeight = 0.075f,
                float maxHeight = 0.7f)
            {
                throw new NotSupportedException();
            }

            public ChunkBuildResult.IsCliff IsSmallCliff(int x, int y)
            {
                return default;
            }
        }

        private sealed class FixedTimeController : ITimeController
        {
            public int DayInMonth => 1;
            public Season Season => Season.Spring;
            public int Year => 0;
            public int Hour => 12;
            public int Minute => 0;
            public float DayProgress => 0.5f;

            public void RestoreTime(
                int dayInMonth,
                Season season,
                int year,
                float dayProgress)
            {
            }

            public void AdvanceDay()
            {
            }

            public void AdvanceMonth()
            {
            }

            public void AdvanceYear()
            {
            }
        }
    }
}
