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
    }
}
#endif
