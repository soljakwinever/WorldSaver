#if UNITY_INCLUDE_TESTS
using System.Linq;
using NUnit.Framework;
using Project.Scripts;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class TownFeatureGenerationTests
    {
        private TownFeatureData town;
        private NodeData door;
        private NodeData bed;
        private NodeData torch;
        private NodeData campfire;
        private NodeData furnace;
        private NodeData villager;

        [SetUp]
        public void SetUp()
        {
            town = ScriptableObject.CreateInstance<TownFeatureData>();
            door = ScriptableObject.CreateInstance<NodeData>();
            bed = ScriptableObject.CreateInstance<NodeData>();
            torch = ScriptableObject.CreateInstance<NodeData>();
            campfire = ScriptableObject.CreateInstance<NodeData>();
            furnace = ScriptableObject.CreateInstance<NodeData>();
            villager = ScriptableObject.CreateInstance<NodeData>();
            town.doorEntity = door;
            town.bedEntity = bed;
            town.torchEntity = torch;
            town.campfireEntity = campfire;
            town.furnaceEntity = furnace;
            town.villagerEntity = villager;
            town.namePrefixes = new[] { "Oak", "River" };
            town.nameSuffixes = new[] { "rest", "ford" };
            town.layoutGenerator = new VillageTownLayoutGenerator
            {
                minimumBuildings = 4,
                maximumBuildings = 4,
                extraRoomChance = 1f
            };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(door);
            Object.DestroyImmediate(bed);
            Object.DestroyImmediate(torch);
            Object.DestroyImmediate(campfire);
            Object.DestroyImmediate(furnace);
            Object.DestroyImmediate(villager);
            Object.DestroyImmediate(town);
        }

        [Test]
        public void VillageLayoutIsDeterministicAndContainsStreets()
        {
            TownLayout first = town.layoutGenerator.Generate(town, 1234, 30);
            TownLayout second = town.layoutGenerator.Generate(town, 1234, 30);

            Assert.That(first.GetCell(Vector2Int.zero), Is.EqualTo(TownCellKind.TownCorePlaza));
            Assert.That(second.GetCell(Vector2Int.zero), Is.EqualTo(first.GetCell(Vector2Int.zero)));
            Assert.That(first.Entities.Select(x => x.id), Is.EqualTo(second.Entities.Select(x => x.id)));
        }

        [Test]
        public void VillageCenterIsAPlazaAndDoorsReplaceBoundaryCells()
        {
            TownLayout layout = town.layoutGenerator.Generate(town, 42, 40);

            Assert.That(layout.GetCell(Vector2Int.zero), Is.EqualTo(TownCellKind.TownCorePlaza));
            TownEntityPlacement[] doors = layout.Entities
                .Where(x => x.entity == door)
                .ToArray();
            Assert.That(doors, Is.Not.Empty);
            foreach (TownEntityPlacement placement in doors)
            {
                Assert.That(layout.GetCell(placement.localCell), Is.EqualTo(TownCellKind.Door));
                Assert.That(placement.usesVillageDoorAccess, Is.True);
            }
        }

        [Test]
        public void VillageCreatesExteriorAndInteriorDoors()
        {
            TownLayout layout = town.layoutGenerator.Generate(town, 42, 40);

            Assert.That(layout.Entities.Count(x => x.id.Contains(":Door")), Is.EqualTo(4));
            Assert.That(layout.Entities.Count(x => x.id.Contains("RoomDoor")), Is.EqualTo(4));
            Assert.That(layout.Entities.Where(x => x.entity == door)
                .All(x => x.usesVillageDoorAccess), Is.True);
        }

        [Test]
        public void EveryHouseContainsBedTorchAndPairedVillager()
        {
            TownLayout layout = town.layoutGenerator.Generate(town, 42, 40);

            TownEntityPlacement[] beds = layout.Entities
                .Where(x => x.entity == bed).ToArray();
            TownEntityPlacement[] torches = layout.Entities
                .Where(x => x.entity == torch).ToArray();
            TownEntityPlacement[] villagers = layout.Entities
                .Where(x => x.entity == villager).ToArray();

            Assert.That(beds, Has.Length.EqualTo(4));
            Assert.That(torches, Has.Length.EqualTo(beds.Length));
            Assert.That(villagers, Has.Length.EqualTo(beds.Length));
            Assert.That(layout.Entities.Select(x => x.localCell).Distinct().Count(),
                Is.EqualTo(layout.Entities.Count));
            foreach (TownEntityPlacement placement in beds.Concat(torches)
                         .Concat(villagers))
            {
                Assert.That(layout.GetCell(placement.localCell),
                    Is.EqualTo(TownCellKind.BuildingFloor));
            }
            foreach (TownEntityPlacement placement in beds)
            {
                Assert.That(placement.ownerPlacementId, Is.Not.Empty);
                Assert.That(villagers.Any(x => x.id == placement.ownerPlacementId),
                    Is.True);
            }
        }

        [Test]
        public void VillageContainsOneCampfireAndOneFurnace()
        {
            TownLayout layout = town.layoutGenerator.Generate(town, 42, 40);

            Assert.That(layout.Entities.Count(x => x.entity == campfire),
                Is.EqualTo(1));
            Assert.That(layout.Entities.Count(x => x.entity == furnace),
                Is.EqualTo(1));
        }

        [TestCase(1, 24)]
        [TestCase(42, 40)]
        [TestCase(9876, 64)]
        public void VillagerCountAlwaysMatchesBedCount(int seed, int radius)
        {
            TownLayout layout = town.layoutGenerator.Generate(town, seed, radius);

            Assert.That(layout.Entities.Count(x => x.entity == villager),
                Is.EqualTo(layout.Entities.Count(x => x.entity == bed)));
        }

        [Test]
        public void TownNameIsStableAndUsesConfiguredVocabulary()
        {
            string name = town.GenerateTownName(9876);

            Assert.That(town.GenerateTownName(9876), Is.EqualTo(name));
            Assert.That(new[] { "Oakrest", "Oakford", "Riverrest", "Riverford" }, Does.Contain(name));
        }
    }
}
#endif
