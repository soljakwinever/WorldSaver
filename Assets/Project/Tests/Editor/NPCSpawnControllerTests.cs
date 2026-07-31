#if UNITY_INCLUDE_TESTS
using System.Reflection;
using NUnit.Framework;
using Project.Scripts;
using Project.Scripts.Gameplay;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class NPCSpawnControllerTests
    {
        [Test]
        public void OpenTerrainIsEligibleForNpcSpawning()
        {
            TerrainSample sample = new()
            {
                isWater = false,
                isCliff = false
            };

            Assert.That(NPCSpawnController.IsNavigableSpawn(sample), Is.True);
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void ImpassableTerrainIsRejectedForNpcSpawning(
            bool isWater,
            bool isCliff)
        {
            TerrainSample sample = new()
            {
                isWater = isWater,
                isCliff = isCliff
            };

            Assert.That(NPCSpawnController.IsNavigableSpawn(sample), Is.False);
        }

        [Test]
        public void NpcWaypointsUseCellCentersInsteadOfUnstableBoundaries()
        {
            MethodInfo method = typeof(TestNPCChaser).GetMethod(
                "CellCenter",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);
            Vector2 center = (Vector2)method.Invoke(
                null,
                new object[] { new Vector2Int(-1, 2) });
            Assert.That(center, Is.EqualTo(new Vector2(-0.5f, 2.5f)));
        }

        [TestCase(129, 100, 110, 30, 20, false)]
        [TestCase(130, 100, 111, 30, 20, false)]
        [TestCase(130, 100, 110, 30, 20, true)]
        public void TransientRecycleRequiresBothLifetimeAndContinuousIdleTime(
            long currentTick,
            long spawnTick,
            long idleSinceTick,
            int minimumLifetimeTicks,
            int minimumIdleTicks,
            bool expected)
        {
            Assert.That(
                NPCSpawnController.ShouldRecycleTransient(
                    currentTick,
                    spawnTick,
                    idleSinceTick,
                    minimumLifetimeTicks,
                    minimumIdleTicks),
                Is.EqualTo(expected));
        }

        [Test]
        public void TransientRecycleRejectsNpcThatIsNotCurrentlyIdle()
        {
            Assert.That(
                NPCSpawnController.ShouldRecycleTransient(
                    currentTick: 1000,
                    spawnTick: 0,
                    idleSinceTick: null,
                    minimumLifetimeTicks: 10,
                    minimumIdleTicks: 10),
                Is.False);
        }
    }
}
#endif
