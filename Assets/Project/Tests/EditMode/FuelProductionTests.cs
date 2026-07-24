#if UNITY_INCLUDE_TESTS
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Gameplay;
using Project.Scripts.Persistence;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class FuelProductionTests
    {
        private GameObject _host;
        private ItemTag _fuelTag;
        private ItemData _fuel;
        private ItemData _output;
        private HasFuelConditionDefinition _hasFuel;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("Fuel production");
            _fuelTag = ScriptableObject.CreateInstance<ItemTag>();
            _fuel = CreateItem("fuel", 20, _fuelTag);
            _output = CreateItem("output", 20);
            _hasFuel = ScriptableObject.CreateInstance<HasFuelConditionDefinition>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_host);
            Object.DestroyImmediate(_fuelTag);
            Object.DestroyImmediate(_fuel);
            Object.DestroyImmediate(_output);
            Object.DestroyImmediate(_hasFuel);
        }

        [Test]
        public void TaggedRemovalIsAtomicAndCrossesRarities()
        {
            PersistentInventory inventory = CreateInventory();
            inventory.TryAdd(_fuel, 2, out _, ItemData.Rarity.Common);
            inventory.TryAdd(_fuel, 3, out _, ItemData.Rarity.Rare);

            Assert.That(inventory.GetCount(_fuelTag), Is.EqualTo(5));
            Assert.That(inventory.TryRemove(_fuelTag, 4), Is.True);
            Assert.That(inventory.GetCount(_fuelTag), Is.EqualTo(1));

            Assert.That(inventory.TryRemove(_fuelTag, 2), Is.False);
            Assert.That(inventory.GetCount(_fuelTag), Is.EqualTo(1));
        }

        [Test]
        public void ProductionConsumesOneFuelPerCompletedCycle()
        {
            PersistentInventory inventory = CreateInventory();
            inventory.TryAdd(_fuel, 2, out _);
            CreateFuelConsumer(inventory);
            PersistentProduction production = CreateProduction();

            production.SimulateOffline(0, 25, OfflineSimulationPolicy.CatchUp);

            Assert.That(inventory.GetCount(_fuelTag), Is.Zero);
            Assert.That(inventory.GetCount(_output), Is.EqualTo(2));
            Assert.That(production.IsConditionSatisfied, Is.False);
        }

        [Test]
        public void HigherValueFuelPowersMultipleCycles()
        {
            _fuel.fuelValue = 3;
            PersistentInventory inventory = CreateInventory();
            inventory.TryAdd(_fuel, 1, out _);
            CreateFuelConsumer(inventory);
            PersistentProduction production = CreateProduction();

            production.SimulateOffline(0, 35, OfflineSimulationPolicy.CatchUp);

            Assert.That(inventory.GetCount(_fuelTag), Is.Zero);
            Assert.That(inventory.GetCount(_output), Is.EqualTo(3));
            Assert.That(production.IsConditionSatisfied, Is.False);
        }

        [Test]
        public void RareFuelAccumulatesFiftyPercentMoreCycles()
        {
            PersistentInventory inventory = CreateInventory();
            inventory.TryAdd(_fuel, 2, out _, ItemData.Rarity.Rare);
            CreateFuelConsumer(inventory);
            PersistentProduction production = CreateProduction();

            production.SimulateOffline(0, 35, OfflineSimulationPolicy.CatchUp);

            Assert.That(inventory.GetCount(_fuelTag), Is.Zero);
            Assert.That(inventory.GetCount(_output), Is.EqualTo(3));
            Assert.That(production.IsConditionSatisfied, Is.False);
        }

        [Test]
        public void VersionOneStoredFuelValueMigratesToFixedPointUnits()
        {
            PersistentInventory inventory = CreateInventory();
            InventoryFuelConsumer consumer = CreateFuelConsumer(inventory);

            using MemoryStream stream = new();
            using (BinaryWriter writer = new(
                       stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(2L);
            }

            stream.Position = 0;
            using BinaryReader reader = new(stream);
            consumer.ReadState(reader, 1);

            Assert.That(consumer.StoredFuelValue, Is.EqualTo(2));
            Assert.That(consumer.AvailableOperations, Is.EqualTo(2));
        }

        [Test]
        public void UnspentFuelValueRoundTrips()
        {
            _fuel.fuelValue = 3;
            PersistentInventory inventory = CreateInventory();
            inventory.TryAdd(_fuel, 1, out _);
            InventoryFuelConsumer source = CreateFuelConsumer(inventory);
            source.Commit(1);

            byte[] state;
            using (MemoryStream stream = new())
            {
                using BinaryWriter writer = new(stream);
                source.WriteState(writer);
                state = stream.ToArray();
            }

            GameObject restoredHost = new("Restored fuel consumer");
            try
            {
                PersistentInventory restoredInventory =
                    restoredHost.AddComponent<PersistentInventory>();
                restoredInventory.Configure(2, new[] { _fuel, _output });
                InventoryFuelConsumer restored =
                    restoredHost.AddComponent<InventoryFuelConsumer>();
                restored.Initialize(_fuelTag, 1);

                using MemoryStream stream = new(state, writable: false);
                using BinaryReader reader = new(stream);
                restored.ReadState(reader, source.PersistentVersion);

                Assert.That(restored.StoredFuelValue, Is.EqualTo(2));
                Assert.That(restored.AvailableOperations, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(restoredHost);
            }
        }

        [Test]
        public void FuelOutagePreservesPartialCycleWithoutBacklog()
        {
            PersistentInventory inventory = CreateInventory();
            inventory.TryAdd(_fuel, 2, out _);
            CreateFuelConsumer(inventory);
            PersistentProduction production = CreateProduction();

            production.SimulateOffline(0, 25, OfflineSimulationPolicy.CatchUp);
            production.SimulateOffline(25, 100, OfflineSimulationPolicy.CatchUp);
            inventory.TryAdd(_fuel, 1, out _);
            production.SimulateOffline(100, 105, OfflineSimulationPolicy.CatchUp);

            Assert.That(inventory.GetCount(_output), Is.EqualTo(3));
            Assert.That(inventory.GetCount(_fuelTag), Is.Zero);
        }

        private PersistentInventory CreateInventory()
        {
            PersistentInventory inventory = _host.AddComponent<PersistentInventory>();
            inventory.Configure(10, new[] { _fuel, _output });
            return inventory;
        }

        private InventoryFuelConsumer CreateFuelConsumer(PersistentInventory inventory)
        {
            Assert.That(inventory, Is.Not.Null);
            InventoryFuelConsumer consumer =
                _host.AddComponent<InventoryFuelConsumer>();
            consumer.Initialize(_fuelTag, 1);
            return consumer;
        }

        private PersistentProduction CreateProduction()
        {
            PersistentProduction production =
                _host.AddComponent<PersistentProduction>();
            production.Initialize(_output, 1, 10, false,
                ItemData.Rarity.Common, _hasFuel);
            return production;
        }

        private static ItemData CreateItem(
            string id,
            int maxStack,
            params ItemTag[] tags)
        {
            ItemData item = ScriptableObject.CreateInstance<ItemData>();
            item.persistentId = id;
            item.maxStack = maxStack;
            typeof(ItemData)
                .GetField("tags", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(item, tags);
            return item;
        }
    }
}
#endif
