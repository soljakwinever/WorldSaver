using System;
using System.Collections.Generic;
using System.IO;
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
        public void CameraWeatherOverrideRejectsRegionalFullScreenEvents()
        {
            Vector2Int cameraRegion = new(2, -1);

            Assert.That(
                WeatherEffectPresenter.ShouldApplyRegionalFullScreenEvent(
                    true,
                    cameraRegion,
                    cameraRegion),
                Is.False);
            Assert.That(
                WeatherEffectPresenter.ShouldApplyRegionalFullScreenEvent(
                    true,
                    cameraRegion,
                    new Vector2Int(3, -1)),
                Is.True);
            Assert.That(
                WeatherEffectPresenter.ShouldApplyRegionalFullScreenEvent(
                    false,
                    cameraRegion,
                    cameraRegion),
                Is.True);
        }

        [TestCase(1000f, 1f, 1000f)]
        [TestCase(1000f, 0.5f, 500f)]
        [TestCase(1000f, 0f, 0f)]
        public void FullScreenEmissionScalesFromAuthoredRate(
            float authoredRate,
            float influence,
            float expected)
        {
            Assert.That(
                WeatherEffectPresenter.ScaleEmissionRate(
                    authoredRate,
                    influence),
                Is.EqualTo(expected));
        }

        [TestCase(0f, 0f)]
        [TestCase(0.5f, 500f)]
        [TestCase(1f, 1000f)]
        public void ConstantParticleEmissionScalesWithSpatialInfluence(
            float influence,
            float expected)
        {
            ParticleSystem.MinMaxCurve authored = new(1000f);
            ParticleSystem.MinMaxCurve scaled =
                WeatherEffectPresenter.ScaleEmissionCurve(
                    authored,
                    influence);

            Assert.That(
                scaled.mode,
                Is.EqualTo(ParticleSystemCurveMode.Constant));
            Assert.That(scaled.constant, Is.EqualTo(expected));
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
        public void ClimateCoreTemperatureIsCircularAndNotRegionWide()
        {
            RecordingWorldGenerator generator = new()
            {
                TemperatureAt = (_, _) => 0.5f
            };
            var cores = new RadialClimateCoreSource(
                new Vector2(64f, 64f),
                innerRadius: 4f,
                spreadRadius: 24f,
                temperatureOffset: -0.8f);
            var service = new RegionalClimateService(
                generator,
                new FixedTimeController(),
                cores,
                _settings);

            ClimateSnapshot regional =
                service.GetCurrentSnapshot(Vector2Int.zero);
            ClimateSnapshot center = service.GetLocalSnapshot(
                new Vector2(64f, 64f),
                0.5f,
                regional);
            ClimateSnapshot falloff = service.GetLocalSnapshot(
                new Vector2(76f, 64f),
                0.5f,
                regional);
            ClimateSnapshot outside = service.GetLocalSnapshot(
                new Vector2(96f, 64f),
                0.5f,
                regional);

            Assert.That(center.Temperature,
                Is.EqualTo(regional.Temperature - 0.8f).Within(0.0001f));
            Assert.That(falloff.Temperature,
                Is.GreaterThan(center.Temperature));
            Assert.That(falloff.Temperature,
                Is.LessThan(regional.Temperature));
            Assert.That(outside.Temperature,
                Is.EqualTo(regional.Temperature).Within(0.0001f));
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
            public Vector2Int WorldSpawnPosition => Vector2Int.zero;

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

            public bool TryFindSafePortalPosition(PlaneData destinationPlane, Vector2Int requestedCell, out Vector2Int safeCell,
                int searchRadius = 64, int clearanceRadius = 2)
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

        private sealed class RadialClimateCoreSource :
            IClimateCoreWeatherSource
        {
            private readonly Vector2 _position;
            private readonly float _innerRadius;
            private readonly float _spreadRadius;
            private readonly float _temperatureOffset;

            public RadialClimateCoreSource(
                Vector2 position,
                float innerRadius,
                float spreadRadius,
                float temperatureOffset)
            {
                _position = position;
                _innerRadius = innerRadius;
                _spreadRadius = spreadRadius;
                _temperatureOffset = temperatureOffset;
            }

            public void Apply(
                Vector2Int region,
                ClimateContext context,
                ref ClimateModifierAccumulator modifiers)
            {
            }

            public void HydrateRegion(RuntimeRegion region)
            {
            }

            public float GetTemperatureOffset(Vector2 worldPosition)
            {
                float distance = Vector2.Distance(_position, worldPosition);
                if (distance <= _innerRadius)
                    return _temperatureOffset;
                if (distance >= _spreadRadius)
                    return 0f;

                float t = Mathf.InverseLerp(
                    _spreadRadius,
                    _innerRadius,
                    distance);
                float influence = t * t * (3f - 2f * t);
                return _temperatureOffset * influence;
            }

            public bool TryGetPermanentWeather(
                Vector2Int region,
                out string weatherId)
            {
                weatherId = string.Empty;
                return false;
            }

            public bool TryGetPermanentWeather(
                Vector2 worldPosition,
                long tick,
                out string weatherId,
                out float influence)
            {
                weatherId = string.Empty;
                influence = 0f;
                return false;
            }

            public bool TryGetPermanentWeather(
                Vector2Int region,
                long tick,
                out string weatherId)
            {
                weatherId = string.Empty;
                return false;
            }

            public bool TryGetPermanentWeather(
                Vector2Int region,
                long tick,
                out string weatherId,
                out float influence)
            {
                weatherId = string.Empty;
                influence = 0f;
                return false;
            }
        }
    }

    public sealed class RegionalWeatherOfflineCoreTests
    {
        [TestCase(0.01f, 3, 0)]
        [TestCase(0.34f, 3, 1)]
        [TestCase(0.67f, 3, 2)]
        [TestCase(1f, 3, 2)]
        public void ForcedWeatherInfluenceSelectsIncreasingPhase(
            float influence,
            int phaseCount,
            int expectedPhase)
        {
            Assert.That(
                RegionalWeatherService.SelectForcedPhaseIndex(
                    influence,
                    phaseCount),
                Is.EqualTo(expectedPhase));
        }

        [Test]
        public void ForcedCoreWeatherAccumulatesSnowDuringOfflineSimulation()
        {
            WeatherSimulationSettings settings =
                Resources.Load<WeatherSimulationSettings>(
                    "Weather/WeatherSimulationSettings");
            Assert.That(settings, Is.Not.Null);
            bool hasPermanentBlizzard = false;
            foreach (Project.Scripts.TimeAndWeather.WeatherData weather in
                     settings.Weather)
            {
                if (weather != null &&
                    weather.WeatherId == "snow.blizzard.perma")
                {
                    hasPermanentBlizzard = true;
                    break;
                }
            }
            Assert.That(
                hasPermanentBlizzard,
                Is.True,
                "ClimateCore permanent weather must be registered so regional sampling cannot replace it.");

            WorldData worldData = ScriptableObject.CreateInstance<WorldData>();
            try
            {
                Type mapSignalBusType = Type.GetType(
                    "Project.Scripts.Bus.MapSignalBus, Playwinever.Bus");
                Assert.That(mapSignalBusType, Is.Not.Null);
                var cores = new AlwaysBlizzardCoreSource();
                var service = (RegionalWeatherService)Activator.CreateInstance(
                    typeof(RegionalWeatherService),
                    new object[]
                    {
                        new OfflineFixedClimateService(),
                        settings,
                        null,
                        new OfflineWeatherClock { CurrentTick = 100 },
                        Activator.CreateInstance(mapSignalBusType),
                        new WeatherBus(),
                        worldData,
                        cores
                    });
                RuntimeRegion region = new(Vector2Int.zero, 0);

                cores.Influence = 0.1f;
                WeatherSample outer = service.Sample(Vector2.zero);
                cores.Influence = 0.2f;
                WeatherSample innerInSamePhase = service.Sample(Vector2.zero);
                Assert.That(outer.PhaseId, Is.EqualTo("snow.light"));
                Assert.That(
                    innerInSamePhase.PhaseId,
                    Is.EqualTo("snow.light"));
                Assert.That(
                    innerInSamePhase.ActiveEffects[0].Intensity,
                    Is.GreaterThan(outer.ActiveEffects[0].Intensity));
                Assert.That(
                    innerInSamePhase.ActiveEffects[0].Intensity,
                    Is.EqualTo(
                        outer.ActiveEffects[0].Intensity * 2f)
                        .Within(0.0001f));

                cores.Influence = 0.5f;
                WeatherSample mediumInfluence = service.Sample(Vector2.zero);
                Assert.That(
                    mediumInfluence.PhaseId,
                    Is.EqualTo("snow.heavy"));
                Assert.That(
                    mediumInfluence.ActiveEffects[0].Intensity,
                    Is.EqualTo(0.5f).Within(0.0001f));
                cores.Influence = 1f;
                WeatherSample fullInfluence = service.Sample(Vector2.zero);
                Assert.That(fullInfluence.PhaseId, Is.EqualTo("snow.blizzard"));
                Assert.That(fullInfluence.HasWeatherOverride, Is.True);
                Assert.That(fullInfluence.WeatherOverrideInfluence, Is.EqualTo(1f));
                Assert.That(
                    fullInfluence.ActiveEffects[0].Intensity,
                    Is.EqualTo(1f));

                IRegionSimulationWork work = service.Prepare(
                    region,
                    0,
                    100,
                    OfflineSimulationPolicy.CatchUp);
                Assert.That(work, Is.Not.Null);
                work.Execute();

                RegionComponentRecord component = region.Components[0x5754];
                using MemoryStream stream =
                    new(component.data, writable: false);
                using BinaryReader reader = new(stream);
                Assert.That(reader.ReadString(), Is.EqualTo("snow.blizzard"));
                Assert.That(reader.ReadInt32(), Is.EqualTo(2));
                reader.ReadInt64();
                reader.ReadInt64();
                reader.ReadInt64();
                reader.ReadInt64();
                reader.ReadUInt32();
                reader.ReadSingle();
                reader.ReadSingle();
                Assert.That(reader.ReadSingle(), Is.GreaterThan(0f));
            }
            finally
            {
                Object.DestroyImmediate(worldData);
            }
        }

        private sealed class AlwaysBlizzardCoreSource :
            IClimateCoreWeatherSource
        {
            public float Influence { get; set; } = 1f;

            public void Apply(
                Vector2Int region,
                ClimateContext context,
                ref ClimateModifierAccumulator modifiers)
            {
            }

            public void HydrateRegion(RuntimeRegion region)
            {
            }

            public float GetTemperatureOffset(Vector2 worldPosition) => -0.75f;

            public bool TryGetPermanentWeather(
                Vector2Int region,
                out string weatherId) =>
                TryGetPermanentWeather(region, 0, out weatherId);

            public bool TryGetPermanentWeather(
                Vector2Int region,
                long tick,
                out string weatherId)
            {
                weatherId = "snow.blizzard";
                return true;
            }

            public bool TryGetPermanentWeather(
                Vector2 worldPosition,
                long tick,
                out string weatherId,
                out float influence)
            {
                weatherId = "snow.blizzard";
                influence = Influence;
                return true;
            }

            public bool TryGetPermanentWeather(
                Vector2Int region,
                long tick,
                out string weatherId,
                out float influence)
            {
                weatherId = "snow.blizzard";
                influence = Influence;
                return true;
            }
        }

        private sealed class OfflineWeatherClock : IWeatherWorldClock
        {
            public long CurrentTick { get; set; }
        }

        private sealed class OfflineFixedClimateService :
            IRegionalClimateService
        {
            private readonly Dictionary<Vector2Int, RegionalClimateBaseline>
                _baselines = new();

            public RegionalClimateBaseline GetBaseline(Vector2Int region)
            {
                if (_baselines.TryGetValue(
                        region,
                        out RegionalClimateBaseline baseline))
                {
                    return baseline;
                }

                baseline = new RegionalClimateBaseline(
                    region,
                    1,
                    0.5f,
                    0.5f,
                    0.5f,
                    0f,
                    1f,
                    1f,
                    1f,
                    0f,
                    0f,
                    0.5f,
                    0f,
                    0f,
                    null,
                    Array.Empty<BiomeClimateWeight>(),
                    1);
                _baselines.Add(region, baseline);
                return baseline;
            }

            public bool TryGetBaseline(
                Vector2Int region,
                out RegionalClimateBaseline baseline)
            {
                baseline = GetBaseline(region);
                return true;
            }

            public ClimateSnapshot GetCurrentSnapshot(Vector2Int region) =>
                GetSnapshot(
                    region,
                    new ClimateContext(Season.Spring, 0, 0.5f));

            public bool TryGetCurrentSnapshot(
                Vector2Int region,
                out ClimateSnapshot snapshot)
            {
                snapshot = GetCurrentSnapshot(region);
                return true;
            }

            public ClimateSnapshot GetSnapshot(
                Vector2Int region,
                ClimateContext context) =>
                new(
                    GetBaseline(region),
                    context,
                    0f,
                    1f,
                    0f,
                    1f);

            public ClimateSnapshot GetLocalSnapshot(
                Vector2 worldPosition,
                ClimateSnapshot regionalSnapshot) =>
                regionalSnapshot;

            public ClimateSnapshot GetLocalSnapshot(
                Vector2 worldPosition,
                float normalizedTerrainTemperature,
                ClimateSnapshot regionalSnapshot) =>
                regionalSnapshot;

            public void ClearCache() => _baselines.Clear();
        }
    }
}
