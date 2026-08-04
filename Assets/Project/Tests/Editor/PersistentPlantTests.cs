#if UNITY_INCLUDE_TESTS
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Project.Scripts;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Enums;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Project.Tests.EditMode
{
    public sealed class PersistentPlantTests
    {
        [Test]
        public void TileWaterSurvivesPersistentRoundTrip()
        {
            GameObject sourceObject = new("Water Source");
            GameObject restoredObject = new("Water Restored");
            try
            {
                PersistentTileWater source = sourceObject.AddComponent<PersistentTileWater>();
                source.Initialize(new Vector2Int(2, -1));
                Vector3Int cell = new(67, -27, 0);
                source.AddWater(cell, 7.5f, 10f);
                using MemoryStream stream = new();
                using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, true))
                    source.WriteState(writer);

                stream.Position = 0;
                PersistentTileWater restored = restoredObject.AddComponent<PersistentTileWater>();
                restored.Initialize(new Vector2Int(2, -1));
                using (BinaryReader reader = new(stream))
                    restored.ReadState(reader, source.PersistentVersion);

                Assert.That(restored.GetWater(cell), Is.EqualTo(7.5f));
                Assert.That(restored.ConsumeWater(cell, 2f), Is.EqualTo(2f));
                Assert.That(restored.GetWater(cell), Is.EqualTo(5.5f));
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(restoredObject);
            }
        }

        [Test]
        public void HourlyDryingUpdatesPersistedWaterAndTileColor()
        {
            GameObject gameObject = new("Drying Soil");
            TileData tile = ScriptableObject.CreateInstance<TileData>();
            try
            {
                tile.usesMoistureTint = true;
                tile.maximumWaterPoints = 10f;
                tile.waterPointsLostPerHour = 2f;
                tile.dryColor = Color.black;
                tile.saturatedColor = Color.white;
                FakeTileContext context = new(tile);
                PersistentTileWater water = gameObject.AddComponent<PersistentTileWater>();
                water.Initialize(Vector2Int.zero, context, 0, ticksPerHour: 10f);
                Vector3Int cell = new(2, 3, 0);
                water.AddWater(cell, 10f, 10f);

                water.SimulateOffline(0, 20, OfflineSimulationPolicy.CatchUp);

                Assert.That(water.GetWater(cell), Is.EqualTo(6f));
                Assert.That(context.LastColor, Is.EqualTo(Color.Lerp(Color.black, Color.white, 0.6f)));
            }
            finally
            {
                Object.DestroyImmediate(tile);
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void WateringWeatherIncreasesSoilMoistureHourly()
        {
            GameObject gameObject = new("Rain Soil");
            TileData tile = ScriptableObject.CreateInstance<TileData>();
            try
            {
                tile.usesMoistureTint = true;
                tile.maximumWaterPoints = 10f;
                tile.waterPointsLostPerHour = 1f;
                tile.rainWaterPointsPerHour = 3f;
                FakeTileContext context = new(tile, wateringWeather: true);
                PersistentTileWater water = gameObject.AddComponent<PersistentTileWater>();
                water.Initialize(Vector2Int.zero, context, 0, ticksPerHour: 10f);

                water.Tick(20);

                Assert.That(water.GetWater(Vector3Int.zero), Is.EqualTo(4f));
            }
            finally
            {
                Object.DestroyImmediate(tile);
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void WateringCanDurabilityCanBeRefilled()
        {
            GameObject gameObject = new("Watering Can Inventory");
            ItemData item = ScriptableObject.CreateInstance<ItemData>();
            try
            {
                item.persistentId = "test.watering-can";
                item.maxStack = 1;
                PersistentInventory inventory = gameObject.AddComponent<PersistentInventory>();
                inventory.Configure(1, new[] { item });
                inventory.TryAdd(item, 1, out _, durability: 5);

                Assert.That(inventory.TryRefillDurability(item), Is.True);
                Assert.That(inventory.Stacks[0].Durability, Is.EqualTo(byte.MaxValue));
            }
            finally
            {
                Object.DestroyImmediate(item);
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void WateringCanHotbarFillUsesPersistedDurability()
        {
            GameObject gameObject = new("Watering Can Fill");
            ItemData item = ScriptableObject.CreateInstance<ItemData>();
            try
            {
                item.persistentId = "test.fill-watering-can";
                item.maxStack = 1;
                typeof(ItemData).GetField("actionData",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(item, new ItemActionData[]
                    {
                        new WaterTileToolActionData { waterPerUse = 10 }
                    });
                PersistentInventory inventory = gameObject.AddComponent<PersistentInventory>();
                inventory.Configure(1, new[] { item });
                inventory.TryAdd(item, 1, out _, durability: 128);
                ItemActionBinding binding = new(item);
                binding.BindFillSource(gameObject);

                Assert.That(binding.DisplayFill, Is.True);
                Assert.That(binding.Fill01, Is.EqualTo(128f / byte.MaxValue));
            }
            finally
            {
                Object.DestroyImmediate(item);
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void InvalidSeasonCatchUpDamagesPlantUntilDeath()
        {
            GameObject gameObject = new("Seasonal Plant");
            PlantData data = ScriptableObject.CreateInstance<PlantData>();
            WorldData world = ScriptableObject.CreateInstance<WorldData>();
            try
            {
                data.maximumHealth = 20;
                data.healthLostPerInvalidSeasonHour = 10f;
                data.healthLostPerDryHour = 0f;
                data.growingSeasons = new[] { Season.Spring };
                world.minutesPerDay = 24f; // 60 ticks per game hour.

                PersistentHealth health = gameObject.AddComponent<PersistentHealth>();
                health.Initialize(data.maximumHealth);
                PersistentPlant plant = gameObject.AddComponent<PersistentPlant>();
                plant.Construct(null, new FakeClock(), new FakeTime(Season.Winter), world);
                plant.Initialize(data, null);

                plant.SimulateOffline(0, 120, OfflineSimulationPolicy.CatchUp);

                Assert.That(health.Health, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(data);
                Object.DestroyImmediate(world);
                Object.DestroyImmediate(gameObject);
            }
        }

        private sealed class FakeClock : IWorldClock
        {
            public long CurrentTick => 0;
            public void Save() { }
        }

        private sealed class FakeTime : ITimeController
        {
            public FakeTime(Season season) => Season = season;
            public int DayInMonth => 1;
            public Season Season { get; }
            public int Year => 1;
            public int Hour => 0;
            public int Minute => 0;
            public float DayProgress => 0f;
            public void RestoreTime(int dayInMonth, Season season, int year, float dayProgress) { }
            public void AdvanceDay() { }
            public void AdvanceMonth() { }
            public void AdvanceYear() { }
        }

        private sealed class FakeTileContext : IPlantTileContext
        {
            private readonly TileData _tile;
            private readonly bool _wateringWeather;
            public Color LastColor { get; private set; }
            public FakeTileContext(TileData tile, bool wateringWeather = false)
            { _tile = tile; _wateringWeather = wateringWeather; }
            public bool TryGetPlantingTile(Vector3Int worldCell, out TileData tile)
            { tile = _tile; return true; }
            public float GetPlantWater(Vector3Int worldCell) => 0f;
            public float AddPlantWater(Vector3Int worldCell, float amount, float maximum) => 0f;
            public float ConsumePlantWater(Vector3Int worldCell, float amount) => 0f;
            public void SetPlantWaterColor(Vector3Int worldCell, Color color) => LastColor = color;
            public bool DoesWeatherWaterPlants(Vector3Int worldCell) => _wateringWeather;
        }
    }
}
#endif
