#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class WorldPartitionTests
    {
        [TestCase(0, 0, 0, 0, 0)]
        [TestCase(7, 7, 0, 0, 63)]
        [TestCase(8, 0, 1, 0, 0)]
        [TestCase(-1, -1, -1, -1, 63)]
        [TestCase(-8, 0, -1, 0, 0)]
        [TestCase(-9, 0, -2, 0, 7)]
        public void GlobalChunkMapsToExpectedRegionAndIndex(
            int chunkX,
            int chunkY,
            int regionX,
            int regionY,
            int localIndex)
        {
            Vector2Int chunk = new(chunkX, chunkY);

            Assert.That(
                WorldPartition.ChunkToRegion(chunk),
                Is.EqualTo(new Vector2Int(regionX, regionY)));
            Assert.That(
                WorldPartition.GetLocalChunkIndex(chunk),
                Is.EqualTo((ushort)localIndex));
        }
    }
}
#endif
