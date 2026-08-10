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

        [SetUp]
        public void SetUp()
        {
            town = ScriptableObject.CreateInstance<TownFeatureData>();
            door = ScriptableObject.CreateInstance<NodeData>();
            town.doorEntity = door;
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
                Assert.That(layout.GetCell(placement.localCell), Is.EqualTo(TownCellKind.Door));
        }

        [Test]
        public void VillageCreatesExteriorAndInteriorDoors()
        {
            TownLayout layout = town.layoutGenerator.Generate(town, 42, 40);

            Assert.That(layout.Entities.Count(x => x.id.Contains(":Door")), Is.EqualTo(4));
            Assert.That(layout.Entities.Count(x => x.id.Contains("RoomDoor")), Is.EqualTo(4));
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
