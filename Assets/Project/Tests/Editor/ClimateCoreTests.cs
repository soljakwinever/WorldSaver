#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using System.IO;
using Project.Scripts;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using Project.Scripts.TimeAndWeather;
using UnityEditor;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class ClimateCoreTests
    {
        [TestCase(0f, 0f)]
        [TestCase(0.5f, 0.5f)]
        [TestCase(1f, 1f)]
        public void CoreWeatherSeedsCoverageFromSpatialInfluence(
            float influence,
            float expectedCoverage)
        {
            CoverageData snow = AssetDatabase.LoadAssetAtPath<CoverageData>(
                "Assets/Project/Data/Coverage/Snow.asset");
            Assert.That(snow, Is.Not.Null);
            WeatherSample sample = new(
                Vector2Int.zero,
                default,
                "snow.blizzard",
                influence > 0f ? "snow.blizzard" : string.Empty,
                influence,
                -1f,
                Color.white,
                0f,
                0f,
                hasWeatherOverride: true,
                weatherOverrideInfluence: influence);

            Assert.That(
                TileCoverageComponent.TryGetWeatherOverrideCoverage(
                    snow,
                    sample,
                    isIndoor: false,
                    out float coverage),
                Is.True);
            Assert.That(coverage, Is.EqualTo(expectedCoverage));
        }

        [Test]
        public void AreaOfEffectSpreadsOfflineAndUsesSmoothFalloff()
        {
            GameObject host = new("Area Of Effect Test");
            try
            {
                AreaOfEffect area = host.AddComponent<AreaOfEffect>();
                area.Initialize(
                    configuredOuterRadius: 100f,
                    configuredInnerRadius: 20f,
                    configuredSpreadRate: 2f);

                area.SimulateOffline(
                    fromTick: 10,
                    toTick: 20,
                    OfflineSimulationPolicy.CatchUp);

                Assert.That(area.SpreadAmount, Is.EqualTo(40f));
                Assert.That(area.GetInfluence(Vector2.zero, Vector2.zero),
                    Is.EqualTo(1f));
                Assert.That(area.GetInfluence(Vector2.zero, new Vector2(40f, 0f)),
                    Is.EqualTo(0f));
                Assert.That(area.GetInfluence(Vector2.zero, new Vector2(30f, 0f)),
                    Is.EqualTo(0.5f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void RegistryRejectsSecondClimateCoreInSameRegion()
        {
            var service =
                new ClimateCoreInfluenceService(new TestWeatherClock());
            ClimateCoreInfluence first = CreateInfluence(
                new NodeId(1),
                new Vector2(10f, 10f));
            ClimateCoreInfluence duplicate = CreateInfluence(
                new NodeId(2),
                new Vector2(20f, 20f));

            Assert.That(service.RegisterOrUpdate(first), Is.True);
            Assert.That(service.RegisterOrUpdate(duplicate), Is.False);
        }

        [Test]
        public void InfluenceContinuesSpreadingAfterRuntimeComponentUnloads()
        {
            var clock = new TestWeatherClock { CurrentTick = 20 };
            var service = new ClimateCoreInfluenceService(clock);
            service.RegisterOrUpdate(new ClimateCoreInfluence(
                new NodeId(1),
                Vector2.zero,
                innerRadius: 10f,
                outerRadius: 100f,
                spreadRate: 2f,
                spreadAmount: 10f,
                atTick: 0,
                temperatureOffset: -0.5f,
                permanentWeatherId: "snow"));

            Assert.That(
                service.GetTemperatureOffset(new Vector2(25f, 0f)),
                Is.LessThan(0f));
            Assert.That(
                service.GetTemperatureOffset(Vector2.zero),
                Is.EqualTo(-0.5f).Within(0.0001f));
            Assert.That(
                service.GetTemperatureOffset(new Vector2(60f, 0f)),
                Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void AffectedRegionRehydratesSpreadingCoreFromAnotherRegion()
        {
            const ushort climateCoreRegionComponentType = 0x4343;
            RuntimeRegion region = new(new Vector2Int(1, 0), 0);
            using MemoryStream stream = new();
            using (BinaryWriter writer = new(
                       stream,
                       System.Text.Encoding.UTF8,
                       leaveOpen: true))
            {
                writer.Write(1);
                writer.Write(1UL);
                writer.Write(0f);
                writer.Write(0f);
                writer.Write(10f);
                writer.Write(600f);
                writer.Write(2f);
                writer.Write(10f);
                writer.Write(0L);
                writer.Write(-0.5f);
                writer.Write("snow");
            }
            region.SetComponent(new RegionComponentRecord
            {
                typeId = climateCoreRegionComponentType,
                version = 1,
                data = stream.ToArray()
            }, markDirty: false);

            var clock = new TestWeatherClock { CurrentTick = 200 };
            var service = new ClimateCoreInfluenceService(clock);
            service.Simulate(
                region,
                fromTick: 0,
                toTick: 200,
                OfflineSimulationPolicy.CatchUp);

            Assert.That(
                service.GetTemperatureOffset(new Vector2(300f, 0f)),
                Is.LessThan(0f));
            Assert.That(
                service.TryGetPermanentWeather(
                    new Vector2Int(1, 0),
                    tick: 123,
                    out _),
                Is.False);
            Assert.That(
                service.TryGetPermanentWeather(
                    new Vector2Int(1, 0),
                    tick: 124,
                    out string weatherId),
                Is.True);
            Assert.That(weatherId, Is.EqualTo("snow"));
            Assert.That(
                service.TryGetPermanentWeather(
                    new Vector2Int(1, 0),
                    tick: 295,
                    out _,
                    out float mediumInfluence),
                Is.True);
            Assert.That(
                RegionalWeatherService.SelectForcedPhaseIndex(
                    mediumInfluence,
                    phaseCount: 3),
                Is.EqualTo(1));
            Assert.That(
                service.TryGetPermanentWeather(
                    Vector2Int.zero,
                    tick: 295,
                    out _,
                    out float fullInfluence),
                Is.True);
            Assert.That(
                RegionalWeatherService.SelectForcedPhaseIndex(
                    fullInfluence,
                    phaseCount: 3),
                Is.EqualTo(2));
            Assert.That(
                service.TryGetPermanentWeather(
                    new Vector2(590f, 0f),
                    tick: 295,
                    out _,
                    out float edgePointInfluence),
                Is.True);
            Assert.That(
                RegionalWeatherService.SelectForcedPhaseIndex(
                    edgePointInfluence,
                    phaseCount: 3),
                Is.EqualTo(0));
            Assert.That(
                service.TryGetPermanentWeather(
                    new Vector2(256f, 0f),
                    tick: 295,
                    out _,
                    out float mediumPointInfluence),
                Is.True);
            Assert.That(
                RegionalWeatherService.SelectForcedPhaseIndex(
                    mediumPointInfluence,
                    phaseCount: 3),
                Is.EqualTo(1));

            RegionComponentRecord saved =
                region.Components[climateCoreRegionComponentType];
            using MemoryStream savedStream =
                new(saved.data, writable: false);
            using BinaryReader reader = new(savedStream);
            Assert.That(reader.ReadInt32(), Is.EqualTo(1));
            Assert.That(reader.ReadUInt64(), Is.EqualTo(1UL));
            reader.ReadSingle();
            reader.ReadSingle();
            reader.ReadSingle();
            reader.ReadSingle();
            reader.ReadSingle();
            Assert.That(reader.ReadSingle(), Is.EqualTo(10f));
            Assert.That(reader.ReadInt64(), Is.EqualTo(0L));
        }

        [Test]
        public void ClimateCoreFeatureBuildingAssetIsRuntimeSpawnable()
        {
            FeatureBuildingData building =
                Resources.Load<FeatureBuildingData>(
                    "WorldGeneration/Features/Climate Core");

            Assert.That(building, Is.Not.Null);
            Assert.That(building.persistentId, Is.EqualTo("ClimateCore"));
            Assert.That(building.regionUniqueKey, Is.EqualTo("ClimateCore"));
            Assert.That(building.entityArchetype, Is.Not.Null);
            Assert.That(building.entityArchetype.NodeData, Is.Not.Null);
        }

        [Test]
        public void RuntimeFeaturePlacementIncludesWorldTick()
        {
            Vector2 first = FeatureBuildingService.GetRuntimeSpawnPosition(
                new Vector2Int(2, -1),
                worldSeed: 1234,
                featureId: "ClimateCore",
                placementOffset: Vector2.zero,
                worldTick: 10);
            Vector2 second = FeatureBuildingService.GetRuntimeSpawnPosition(
                new Vector2Int(2, -1),
                worldSeed: 1234,
                featureId: "ClimateCore",
                placementOffset: Vector2.zero,
                worldTick: 11);

            Assert.That(second, Is.Not.EqualTo(first));
        }

        private static ClimateCoreInfluence CreateInfluence(
            NodeId id,
            Vector2 position)
        {
            return new ClimateCoreInfluence(
                id,
                position,
                innerRadius: 0f,
                outerRadius: 10f,
                spreadRate: 0f,
                spreadAmount: 10f,
                atTick: 0,
                temperatureOffset: 1f,
                permanentWeatherId: string.Empty);
        }

        private sealed class TestWeatherClock : IWeatherWorldClock
        {
            public long CurrentTick { get; set; }
        }
    }
}
#endif
