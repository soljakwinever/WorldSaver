#if UNITY_INCLUDE_TESTS
using System.IO;
using NUnit.Framework;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Persistence;
using UnityEngine;

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
                layer = PersistentTileLayer.Water,
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
    }
}
#endif
