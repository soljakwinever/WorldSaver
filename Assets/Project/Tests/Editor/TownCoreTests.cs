using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Gameplay;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class TownCoreTests
    {
        private GameObject _gameObject;
        private ItemData _offering;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("Town Core Test");
            _gameObject.SetActive(false);

            _offering = ScriptableObject.CreateInstance<ItemData>();
            _offering.persistentId = "test-offering";
            _offering.maxStack = 99;
            _offering.magicValue = 4f;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_gameObject);
            UnityEngine.Object.DestroyImmediate(_offering);
        }

        [Test]
        public void OfflineTicksAccumulateManaAndConsumeRarityAdjustedOfferings()
        {
            TownCore town = CreateTown(maximumPopulation: 2);
            PersistentInventory inventory =
                _gameObject.GetComponent<PersistentInventory>();
            inventory.Configure(4, new[] { _offering });
            Assert.That(
                inventory.TryAdd(
                    _offering,
                    2,
                    out int remainder,
                    ItemData.Rarity.Rare),
                Is.True);
            Assert.That(remainder, Is.Zero);

            town.SimulateOffline(0, 20, OfflineSimulationPolicy.CatchUp);

            Assert.That(town.ManaPool, Is.EqualTo(14f).Within(0.0001f));
            Assert.That(
                inventory.GetCount(_offering, ItemData.Rarity.Rare),
                Is.Zero);
        }

        [Test]
        public void PopulationHonoursCapacityAndPersistentStateRoundTrips()
        {
            TownCore source = CreateTown(maximumPopulation: 1);
            source.SetName("Oakrest");
            Assert.That(source.TryRegisterResident("villager-a"), Is.True);
            Assert.That(source.TryRegisterResident("villager-b"), Is.False);

            byte[] state;
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                    source.WriteState(writer);
                state = stream.ToArray();
            }

            GameObject restoredObject = new("Restored Town");
            restoredObject.SetActive(false);
            try
            {
                TownCore restored = ConfigureTown(
                    restoredObject,
                    maximumPopulation: 1);
                using var stream = new MemoryStream(state, writable: false);
                using var reader = new BinaryReader(stream);
                restored.ReadState(reader, source.PersistentVersion);

                Assert.That(restored.TownName, Is.EqualTo("Oakrest"));
                Assert.That(restored.Population, Is.EqualTo(1));
                Assert.That(restored.ResidentIds, Contains.Item("villager-a"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(restoredObject);
            }
        }

        [Test]
        public void ProceduralResidentsReceiveStarterFoodOnlyOnce()
        {
            TownCore town = ConfigureTown(
                _gameObject,
                maximumPopulation: 2,
                starterFood: _offering,
                starterFoodPerResident: 2,
                procedural: true);
            TownStockpile stockpile =
                _gameObject.GetComponent<TownStockpile>();

            Assert.That(town.TryRegisterResident("villager-a"), Is.True);
            Assert.That(stockpile.GetCount(_offering), Is.EqualTo(2));
            Assert.That(town.TryRegisterResident("villager-a"), Is.False);
            Assert.That(stockpile.GetCount(_offering), Is.EqualTo(2));
            Assert.That(town.TryRegisterResident("villager-b"), Is.True);
            Assert.That(stockpile.GetCount(_offering), Is.EqualTo(4));
        }

        [Test]
        public void ClaimedAndResourceAreasUseTheirConfiguredRadii()
        {
            TownCore town = CreateTown(maximumPopulation: 1);

            Assert.That(town.ContainsTownPosition(new Vector3(3f, 4f)), Is.True);
            Assert.That(town.ContainsTownPosition(new Vector3(6f, 0f)), Is.False);
            Assert.That(
                town.ContainsResourcePosition(new Vector3(9f, 0f)),
                Is.True);
            Assert.That(
                town.ContainsResourcePosition(new Vector3(11f, 0f)),
                Is.False);
        }

        [Test]
        public void PersistentHealthRoundTripsAnUpgradedMaximum()
        {
            PersistentHealth source =
                _gameObject.AddComponent<PersistentHealth>();
            source.Initialize(100);
            source.SetMaxHealth(150);

            byte[] state;
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(
                           stream,
                           System.Text.Encoding.UTF8,
                           true))
                    source.WriteState(writer);
                state = stream.ToArray();
            }

            GameObject restoredObject = new("Restored Health");
            try
            {
                PersistentHealth restored =
                    restoredObject.AddComponent<PersistentHealth>();
                restored.Initialize(100);
                using var stream = new MemoryStream(state, writable: false);
                using var reader = new BinaryReader(stream);
                restored.ReadState(reader, source.PersistentVersion);

                Assert.That(restored.MaxHealth, Is.EqualTo(150));
                Assert.That(restored.Health, Is.EqualTo(150));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(restoredObject);
            }
        }

        [Test]
        public void RepairEffectChargesOnlyActiveRepairTicksAndPausesManaGain()
        {
            RepairTownEffect effect =
                ScriptableObject.CreateInstance<RepairTownEffect>();
            GameObject building = new("Damaged Building");
            var tileSource = new TestTileRepairSource(3);
            try
            {
                SetField(effect, "persistentId", "repair-structures");
                SetField(effect, "manaCostPerTick", 1f);
                SetField(effect, "healthPerTick", 2);

                TownCore town = ConfigureTown(
                    _gameObject,
                    maximumPopulation: 1,
                    effects: new TownEffect[] { effect });
                Assert.That(
                    town.SetEffectActive(effect.PersistentId, true),
                    Is.True);

                PersistentHealth health =
                    building.AddComponent<PersistentHealth>();
                health.Initialize(10);
                health.TakeDamage(3);
                GetBuildings(town).Add(building);
                TownTileRepairRegistry.Register(tileSource);

                town.SimulateOffline(
                    0,
                    30,
                    OfflineSimulationPolicy.CatchUp);

                Assert.That(health.Health, Is.EqualTo(10));
                Assert.That(tileSource.MissingHealth, Is.Zero);
                // Starting empty, the town idles until each repair tick is
                // affordable. The two active ticks do not replenish mana.
                Assert.That(town.ManaPool, Is.EqualTo(0.8f).Within(0.0001f));

                town.SimulateOffline(
                    30,
                    40,
                    OfflineSimulationPolicy.CatchUp);

                // An enabled effect with no repair work does not cost mana or
                // block replenishment.
                Assert.That(town.ManaPool, Is.EqualTo(1.8f).Within(0.0001f));
            }
            finally
            {
                TownTileRepairRegistry.Unregister(tileSource);
                UnityEngine.Object.DestroyImmediate(building);
                UnityEngine.Object.DestroyImmediate(effect);
            }
        }

        [Test]
        public void ResourceRadiusIncludesThreeUnitSearchBonus()
        {
            TownCore town = CreateTown(maximumPopulation: 10);
            Assert.That(town.ResourceRadius, Is.EqualTo(13f));
        }

        private TownCore CreateTown(int maximumPopulation) =>
            ConfigureTown(_gameObject, maximumPopulation);

        private static TownCore ConfigureTown(
            GameObject gameObject,
            int maximumPopulation,
            TownEffect[] effects = null,
            ItemData starterFood = null,
            int starterFoodPerResident = 0,
            bool procedural = false)
        {
            PersistentInventory inventory =
                gameObject.GetComponent<PersistentInventory>() ??
                gameObject.AddComponent<PersistentInventory>();
            inventory.Initialize(4);

            TownStockpile stockpile =
                gameObject.GetComponent<TownStockpile>() ??
                gameObject.AddComponent<TownStockpile>();
            stockpile.Initialize(8);

            PersistentHealth health =
                gameObject.GetComponent<PersistentHealth>() ??
                gameObject.AddComponent<PersistentHealth>();
            health.Initialize(100);

            TownCore town =
                gameObject.GetComponent<TownCore>() ??
                gameObject.AddComponent<TownCore>();
            town.Initialize(
                "New Village",
                initialTownRadius: 5f,
                initialResourceRadius: 10f,
                maximumPopulation,
                maximumMana: 100f,
                manaPerTick: 0.1f,
                offeringIntervalTicks: 10,
                structureRefreshTicks: 10,
                structureLayerMask: ~0,
                effects: effects ?? Array.Empty<TownEffect>(),
                upgradeDefinitions: Array.Empty<TownUpgradeDefinition>(),
                grantStarterFood: procedural,
                configuredStarterFood: starterFood,
                configuredStarterFoodPerResident: starterFoodPerResident);
            return town;
        }

        private static List<GameObject> GetBuildings(TownCore town)
        {
            FieldInfo field = typeof(TownCore).GetField(
                "_buildings",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (List<GameObject>)field.GetValue(town);
        }

        private static void SetField(
            object target,
            string fieldName,
            object value)
        {
            Type type = target.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }

                type = type.BaseType;
            }

            throw new MissingFieldException(
                target.GetType().FullName,
                fieldName);
        }

        private sealed class TestTileRepairSource : ITownTileRepairSource
        {
            public TestTileRepairSource(int missingHealth)
            {
                MissingHealth = missingHealth;
            }

            public int MissingHealth { get; private set; }

            public int GetMaximumMissingHealth(
                Vector2 townCenter,
                float townRadius) =>
                MissingHealth;

            public int RepairDamagedTiles(
                Vector2 townCenter,
                float townRadius,
                int healthPerTile)
            {
                if (MissingHealth <= 0 || healthPerTile <= 0)
                    return 0;

                MissingHealth = Mathf.Max(0, MissingHealth - healthPerTile);
                return 1;
            }
        }
    }
}
