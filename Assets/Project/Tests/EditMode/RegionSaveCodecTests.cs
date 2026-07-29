#if UNITY_INCLUDE_TESTS
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Project.Scripts;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Enums;
using Project.Scripts.Interface;
using Project.Scripts.Persistence;
using Project.Scripts.TimeAndWeather;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Project.Tests.EditMode
{
    public sealed class RegionSaveCodecTests
    {
        [Test]
        public void TombstoneSurvivesBinaryRoundTrip()
        {
            NodeId id = new(0x1234UL);
            RegionSaveData original = new()
            {
                coordinate = new Vector2Int(-2, 3),
                lastSimulatedTick = 400
            };

            ChunkState chunk = new()
            {
                localChunkIndex = 17,
                lastSimulatedTick = 400
            };
            chunk.entities.Add(PersistentEntityRecord.CreateTombstone(
                id,
                EntityPersistenceKind.Procedural));
            original.changedChunks.Add(chunk);

            using MemoryStream stream = new();
            RegionSaveCodec.Write(stream, original);
            stream.Position = 0;    
            RegionSaveData restored = RegionSaveCodec.Read(stream);

            Assert.That(restored.coordinate, Is.EqualTo(original.coordinate));
            Assert.That(restored.changedChunks, Has.Count.EqualTo(1));
            Assert.That(restored.changedChunks[0].entities, Has.Count.EqualTo(1));
            Assert.That(restored.changedChunks[0].entities[0].id, Is.EqualTo(id));
            Assert.That(
                restored.changedChunks[0].entities[0].existenceState,
                Is.EqualTo(EntityExistenceState.Removed));
        }

        [Test]
        public void TileOverridesSurviveBinaryRoundTrip()
        {
            RegionSaveData original = new()
            {
                coordinate = new Vector2Int(-1, 0)
            };
            ChunkState chunk = new() { localChunkIndex = 7 };
            chunk.tileOverrides.Add(new TileOverrideData
            {
                localX = 31,
                localY = 0,
                layer = PersistentTileLayer.Ground,
                kind = TileOverrideKind.Place,
                tileId = 4
            });
            chunk.tileOverrides.Add(new TileOverrideData
            {
                localX = 0,
                localY = 31,
                layer = PersistentTileLayer.Wall,
                kind = TileOverrideKind.Clear,
                tileId = -1
            });
            original.changedChunks.Add(chunk);

            using MemoryStream stream = new();
            RegionSaveCodec.Write(stream, original);
            stream.Position = 0;
            RegionSaveData restored = RegionSaveCodec.Read(stream);

            Assert.That(restored.changedChunks[0].tileOverrides, Has.Count.EqualTo(2));
            Assert.That(restored.changedChunks[0].tileOverrides[0].tileId, Is.EqualTo(4));
            Assert.That(
                restored.changedChunks[0].tileOverrides[1].layer,
                Is.EqualTo(PersistentTileLayer.Wall));
            Assert.That(
                restored.changedChunks[0].tileOverrides[1].kind,
                Is.EqualTo(TileOverrideKind.Clear));
        }

        [Test]
        public void VersionOneSaveLoadsWithNoTileOverrides()
        {
            using MemoryStream stream = new();
            using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(0x47525357u);
                writer.Write((ushort)1);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0L);
                writer.Write(0L);
                writer.Write(0); // Region components.
                writer.Write(1); // Chunks.
                writer.Write((ushort)0);
                writer.Write(0L);
                writer.Write(0); // Entities.
                writer.Write(0); // Chunk components.
            }

            stream.Position = 0;
            RegionSaveData restored = RegionSaveCodec.Read(stream);

            Assert.That(restored.changedChunks, Has.Count.EqualTo(1));
            Assert.That(restored.changedChunks[0].tileOverrides, Is.Empty);
        }

        [Test]
        public void CompactKeepsLatestOverrideForEachLayerAndCell()
        {
            ChunkState chunk = new();
            chunk.tileOverrides.Add(new TileOverrideData
            {
                localX = 3,
                localY = 4,
                layer = PersistentTileLayer.Ground,
                kind = TileOverrideKind.Place,
                tileId = 1
            });
            chunk.tileOverrides.Add(new TileOverrideData
            {
                localX = 3,
                localY = 4,
                layer = PersistentTileLayer.Ground,
                kind = TileOverrideKind.Clear,
                tileId = -1
            });

            chunk.Compact();

            Assert.That(chunk.tileOverrides, Has.Count.EqualTo(1));
            Assert.That(chunk.tileOverrides[0].kind, Is.EqualTo(TileOverrideKind.Clear));
        }

        [Test]
        public void ChunkComponentPayloadSurvivesBinaryRoundTrip()
        {
            RegionSaveData original = new()
            {
                coordinate = new Vector2Int(2, -4)
            };
            ChunkState chunk = new() { localChunkIndex = 9 };
            chunk.components.Add(new PersistenceComponentRecord
            {
                typeId = TileCoverageComponent.TypeId,
                version = 1,
                data = new byte[] { 1, 4, 9, 16, 25 }
            });
            original.changedChunks.Add(chunk);

            using MemoryStream stream = new();
            RegionSaveCodec.Write(stream, original);
            stream.Position = 0;
            RegionSaveData restored = RegionSaveCodec.Read(stream);

            Assert.That(restored.changedChunks, Has.Count.EqualTo(1));
            Assert.That(restored.changedChunks[0].components, Has.Count.EqualTo(1));
            Assert.That(
                restored.changedChunks[0].components[0].typeId,
                Is.EqualTo(TileCoverageComponent.TypeId));
            Assert.That(
                restored.changedChunks[0].components[0].data,
                Is.EqualTo(new byte[] { 1, 4, 9, 16, 25 }));
        }

        [Test]
        public void CoverageConditionsMatchWeatherPhaseEffectAndTemperature()
        {
            CoverageData coverage =
                ScriptableObject.CreateInstance<CoverageData>();
            StandardWeatherEffectData effect =
                ScriptableObject.CreateInstance<StandardWeatherEffectData>();
            try
            {
                SetField(
                    coverage,
                    "ambientTemperatureRange",
                    new Vector2(-1f, -0.1f));
                SetField(
                    coverage,
                    "allowedWeatherIds",
                    new[] { "snow.heavy" });
                SetField(
                    coverage,
                    "allowedPhaseIds",
                    new[] { "snow.peak" });
                SetField(
                    coverage,
                    "requiredActiveEffectIds",
                    new[] { "precipitation.snow" });
                SetField(effect, "effectId", "precipitation.snow");

                ClimateSnapshot climate = new(
                    null,
                    new ClimateContext(Season.Winter, 1, 0.5f),
                    -0.5f,
                    0.5f,
                    0f,
                    1f);
                WeatherSample sample = new(
                    Vector2Int.zero,
                    climate,
                    "snow.heavy",
                    "snow.peak",
                    1f,
                    -0.5f,
                    Color.white,
                    0f,
                    0.5f,
                    new[] { new WeatherEffectSample(effect, 1f) });

                Assert.That(
                    TileCoverageComponent.ConditionsAllow(
                        coverage,
                        sample),
                    Is.True);

                WeatherSample warm = new(
                    Vector2Int.zero,
                    climate,
                    "snow.heavy",
                    "snow.peak",
                    1f,
                    0.2f,
                    Color.white,
                    0f,
                    0.5f,
                    new[] { new WeatherEffectSample(effect, 1f) });
                Assert.That(
                    TileCoverageComponent.ConditionsAllow(
                        coverage,
                        warm),
                    Is.False);
                Assert.That(
                    TileCoverageComponent.WeatherAllowsAccumulation(
                        coverage,
                        warm),
                    Is.True);
                Assert.That(
                    TileCoverageComponent.TemperatureAllowsPersistence(
                        coverage,
                        warm),
                    Is.False);

                WeatherSample coldAndClear = new(
                    Vector2Int.zero,
                    climate,
                    "clear",
                    string.Empty,
                    1f,
                    -0.5f,
                    Color.white,
                    0f,
                    0f);
                Assert.That(
                    TileCoverageComponent.TemperatureAllowsPersistence(
                        coverage,
                        coldAndClear),
                    Is.True);
                Assert.That(
                    TileCoverageComponent.WeatherAllowsAccumulation(
                        coverage,
                        coldAndClear),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(coverage);
                Object.DestroyImmediate(effect);
            }
        }

        [TestCase(false, true, true)]
        [TestCase(false, false, false)]
        [TestCase(true, true, false)]
        [TestCase(true, false, false)]
        public void CoverageDoesNotAccumulateInsideRooms(
            bool isRoomInterior,
            bool weatherAllowsAccumulation,
            bool expected)
        {
            Assert.That(
                TileCoverageComponent.AllowsWeatherAccumulation(
                    isRoomInterior,
                    weatherAllowsAccumulation),
                Is.EqualTo(expected));
        }

        [Test]
        public void ChunkPersistenceRootCapturesAndRestoresChunkComponents()
        {
            GameObject gameObject = new("Chunk Persistence Test");
            try
            {
                ChunkPersistenceRoot root =
                    gameObject.AddComponent<ChunkPersistenceRoot>();
                TestChunkComponent component =
                    gameObject.AddComponent<TestChunkComponent>();
                component.Value = 0.625f;

                root.BeginRestore(new Vector2Int(3, 4));
                root.CompleteRestore();
                ChunkState snapshot = root.Capture(25);

                Assert.That(snapshot.components, Has.Count.EqualTo(1));
                component.Value = 0f;
                root.BeginRestore(new Vector2Int(3, 4));
                root.Restore(snapshot);

                Assert.That(component.Value, Is.EqualTo(0.625f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        private static void SetField(
            object target,
            string fieldName,
            object value)
        {
            FieldInfo field = null;
            for (System.Type type = target.GetType();
                 type != null && field == null;
                 type = type.BaseType)
            {
                field = type.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic);
            }
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }

        public sealed class TestChunkComponent :
            MonoBehaviour,
            IPersistentComponent
        {
            public float Value;
            public ushort PersistentTypeId => 0x7454;
            public ushort PersistentVersion => 1;

            public void WriteState(BinaryWriter writer) =>
                writer.Write(Value);

            public void ReadState(
                BinaryReader reader,
                ushort savedVersion)
            {
                Assert.That(savedVersion, Is.EqualTo(1));
                Value = reader.ReadSingle();
            }

            public bool IsAtBaseline() => false;
        }
    }
}
#endif
