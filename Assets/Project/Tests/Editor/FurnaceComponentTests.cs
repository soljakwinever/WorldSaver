#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Project.Tests.EditMode
{
    public sealed class FurnaceComponentTests
    {
        private readonly List<Object> _objects = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
                Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
        }

        [Test]
        public void EqualIngredientRaritiesAlwaysProduceThatRarity()
        {
            ItemData ingredient = CreateItem("ingredient", 20);
            IItemStack[] consumed =
            {
                new ItemStack(
                    ingredient, 1, ItemData.Rarity.Rare),
                new ItemStack(
                    ingredient, 3, ItemData.Rarity.Rare)
            };

            Assert.That(
                FurnaceComponent.DetermineOutputRarity(
                    consumed, 0f),
                Is.EqualTo(ItemData.Rarity.Rare));
            Assert.That(
                FurnaceComponent.DetermineOutputRarity(
                    consumed, 0.5f),
                Is.EqualTo(ItemData.Rarity.Rare));
            Assert.That(
                FurnaceComponent.DetermineOutputRarity(
                    consumed, 1f),
                Is.EqualTo(ItemData.Rarity.Rare));
        }

        [Test]
        public void MixedRarityUsesLowestAsFloorAndWeightsHigherQuality()
        {
            ItemData ingredient = CreateItem("ingredient", 20);
            IItemStack[] consumed =
            {
                new ItemStack(
                    ingredient, 1, ItemData.Rarity.Common),
                new ItemStack(
                    ingredient, 1, ItemData.Rarity.Legendary)
            };

            Assert.That(
                FurnaceComponent.DetermineOutputRarity(
                    consumed, 0.1f),
                Is.EqualTo(ItemData.Rarity.Common));
            Assert.That(
                FurnaceComponent.DetermineOutputRarity(
                    consumed, 0.2f),
                Is.EqualTo(ItemData.Rarity.Legendary));
        }

        [Test]
        public void FurnaceAutomaticallyCraftsRecipeWithIngredientRarity()
        {
            EntityTag fuelTag = CreateTag();
            ItemData fuel = CreateItem("fuel", 20, fuelTag);
            ItemData ingredient = CreateItem("ore", 20);
            ItemData output = CreateItem("bar", 20);
            CraftingRecipeData recipe = CreateRecipe(
                ingredient, 2, output);
            RecipeList recipes = CreateRecipeList(recipe);
            GameObject host = new("Furnace");
            _objects.Add(host);
            FurnaceComponent furnace =
                host.AddComponent<FurnaceComponent>();
            furnace.Initialize(
                "Furnace",
                "Use furnace",
                fuelTag,
                recipes,
                2,
                10,
                1);

            Inventory source = new(2);
            source.TryAdd(fuel, 1, out _);
            source.TryAdd(
                ingredient,
                2,
                out _,
                ItemData.Rarity.Rare);

            Assert.That(
                furnace.TryInsertFuel(
                    source,
                    new ItemStack(fuel, 1)),
                Is.True);
            Assert.That(
                furnace.TryInsertIngredient(
                    source,
                    new ItemStack(
                        ingredient,
                        2,
                        ItemData.Rarity.Rare)),
                Is.True);

            furnace.SimulateOffline(
                0, 10, OfflineSimulationPolicy.CatchUp);

            Assert.That(
                furnace.OutputInventory.GetCount(
                    output, ItemData.Rarity.Rare),
                Is.EqualTo(1));
            Assert.That(
                furnace.IngredientInventory.OccupiedSlots,
                Is.Zero);
            Assert.That(
                furnace.FuelInventory.OccupiedSlots,
                Is.Zero);
        }

        [Test]
        public void FurnaceRejectsItemsNotUsedByItsRecipeList()
        {
            EntityTag fuelTag = CreateTag();
            ItemData ingredient = CreateItem("ore", 20);
            ItemData unrelated = CreateItem("unrelated", 20);
            ItemData output = CreateItem("bar", 20);
            RecipeList recipes = CreateRecipeList(
                CreateRecipe(ingredient, 1, output));
            GameObject host = new("Furnace");
            _objects.Add(host);
            FurnaceComponent furnace =
                host.AddComponent<FurnaceComponent>();
            furnace.Initialize(
                "Furnace",
                "Use furnace",
                fuelTag,
                recipes,
                1,
                10,
                1);
            Inventory source = new(1);
            source.TryAdd(unrelated, 1, out _);

            Assert.That(
                furnace.TryInsertIngredient(
                    source,
                    new ItemStack(unrelated, 1)),
                Is.False);
            Assert.That(
                source.GetCount(
                    unrelated, ItemData.Rarity.Common),
                Is.EqualTo(1));
        }

        [Test]
        public void InvalidSavedIngredientDiscardsFurnaceWithoutThrowing()
        {
            EntityTag fuelTag = CreateTag();
            ItemData validIngredient = CreateItem("valid.ore", 20);
            ItemData invalidIngredient =
                CreateItem("materials.iron.ore", 20);
            ItemData output = CreateItem("bar", 20);
            RecipeList recipes = CreateRecipeList(
                CreateRecipe(validIngredient, 1, output));
            GameObject host = new("Invalid furnace");
            _objects.Add(host);
            FurnaceComponent furnace =
                host.AddComponent<FurnaceComponent>();
            FakePersistentEntity persistentEntity = new();
            furnace.PersistentEntity = persistentEntity;
            furnace.Initialize(
                "Furnace",
                "Use furnace",
                fuelTag,
                recipes,
                1,
                10,
                1);

            GameObject catalogHost = new("Catalog");
            catalogHost.SetActive(false);
            _objects.Add(catalogHost);
            ItemCatalog catalog =
                catalogHost.AddComponent<ItemCatalog>();
            typeof(ItemCatalog)
                .GetField(
                    "items",
                    BindingFlags.Instance |
                    BindingFlags.NonPublic)
                ?.SetValue(
                    catalog,
                    new[] { invalidIngredient });
            typeof(FurnaceComponent)
                .GetField(
                    "_catalog",
                    BindingFlags.Instance |
                    BindingFlags.NonPublic)
                ?.SetValue(furnace, catalog);

            using MemoryStream stream = new();
            using (BinaryWriter writer = new(
                       stream, System.Text.Encoding.UTF8,
                       leaveOpen: true))
            {
                writer.Write(0L);
                writer.Write(0L);
                writer.Write(0L);
                writer.Write(0);
                writer.Write(1);
                writer.Write(invalidIngredient.persistentId);
                writer.Write((byte)ItemData.Rarity.Common);
                writer.Write(1);
                writer.Write(0);
            }
            stream.Position = 0;
            using BinaryReader reader = new(stream);

            LogAssert.Expect(
                LogType.Warning,
                new Regex("Discarding furnace entity.*"));
            Assert.That(
                () => furnace.ReadState(reader, 2),
                Throws.Nothing);
            Assert.That(persistentEntity.Removed, Is.True);
            Assert.That(host.activeSelf, Is.False);
        }

        private EntityTag CreateTag()
        {
            EntityTag tag =
                ScriptableObject.CreateInstance<EntityTag>();
            _objects.Add(tag);
            return tag;
        }

        private ItemData CreateItem(
            string id,
            int maxStack,
            params EntityTag[] tags)
        {
            ItemData item =
                ScriptableObject.CreateInstance<ItemData>();
            item.persistentId = id;
            item.maxStack = maxStack;
            typeof(ItemData)
                .GetField(
                    "tags",
                    BindingFlags.Instance |
                    BindingFlags.NonPublic)
                ?.SetValue(item, tags);
            _objects.Add(item);
            return item;
        }

        private CraftingRecipeData CreateRecipe(
            ItemData ingredient,
            int count,
            ItemData output)
        {
            CraftingRecipeData recipe =
                ScriptableObject.CreateInstance<CraftingRecipeData>();
            recipe.ingredients = new[]
            {
                new CraftingRecipeData.RecipeIngredient
                {
                    matchType = CraftingRecipeData
                        .IngredientMatchType.ExactItem,
                    itemData = ingredient,
                    count = count
                }
            };
            recipe.output = new[]
            {
                new CraftingRecipeData.RecipeOutput
                {
                    itemData = output,
                    amount = 1,
                    chance = 1f,
                    rolls = 1
                }
            };
            _objects.Add(recipe);
            return recipe;
        }

        private RecipeList CreateRecipeList(
            params CraftingRecipeData[] recipes)
        {
            RecipeList list =
                ScriptableObject.CreateInstance<RecipeList>();
            typeof(RecipeList)
                .GetField(
                    "recipes",
                    BindingFlags.Instance |
                    BindingFlags.NonPublic)
                ?.SetValue(list, recipes);
            _objects.Add(list);
            return list;
        }

        private sealed class FakePersistentEntity : IPersistentEntity
        {
            public NodeId Id { get; }
            public bool Removed { get; private set; }

            public void RemoveFromWorld()
            {
                Removed = true;
            }
        }
    }
}
#endif
