#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Project.Scripts;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Project.Tests.EditMode
{
    public sealed class CraftingServiceTests
    {
        private readonly List<Object> _objects = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _objects.Count; i++)
                Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
        }

        [Test]
        public void MixedRecipeReservesExactItemsBeforeTagIngredients()
        {
            EntityTag wood = CreateTag();
            ItemData oak = CreateItem("oak", 10, wood);
            ItemData pine = CreateItem("pine", 10, wood);
            ItemData resultItem = CreateItem("result", 10);
            CraftingRecipeData recipe = CreateRecipe(
                new[]
                {
                    Exact(oak, 2),
                    Tagged(wood, 3)
                },
                Output(resultItem, 1));
            Inventory inventory = new(3);
            inventory.TryAdd(oak, 2, out _);
            inventory.TryAdd(pine, 3, out _);

            CraftingService service = new(new SequenceRandom());
            bool crafted = service.TryCraft(
                recipe, inventory, inventory, out CraftResult result);

            Assert.That(crafted, Is.True);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(
                inventory.GetCount(oak, ItemData.Rarity.Common), Is.Zero);
            Assert.That(
                inventory.GetCount(pine, ItemData.Rarity.Common), Is.Zero);
            Assert.That(
                inventory.GetCount(resultItem, ItemData.Rarity.Common), Is.EqualTo(1));
        }

        [Test]
        public void TagIngredientConsumesLowestRarityFirst()
        {
            EntityTag wood = CreateTag();
            ItemData log = CreateItem("log", 10, wood);
            ItemData resultItem = CreateItem("result", 10);
            CraftingRecipeData recipe = CreateRecipe(
                new[] { Tagged(wood, 3) },
                Output(resultItem, 1));
            Inventory inventory = new(3);
            inventory.TryAdd(log, 2, out _, ItemData.Rarity.Common);
            inventory.TryAdd(log, 2, out _, ItemData.Rarity.Rare);

            new CraftingService(new SequenceRandom()).TryCraft(
                recipe, inventory, inventory, out _);

            Assert.That(
                inventory.GetCount(log, ItemData.Rarity.Common), Is.Zero);
            Assert.That(
                inventory.GetCount(log, ItemData.Rarity.Rare), Is.EqualTo(1));
        }

        [Test]
        public void ConsumedStackCanFreeSlotForOutput()
        {
            ItemData ingredient = CreateItem("ingredient", 1);
            ItemData output = CreateItem("output", 1);
            CraftingRecipeData recipe = CreateRecipe(
                new[] { Exact(ingredient, 1) },
                Output(output, 1));
            Inventory inventory = new(1);
            inventory.TryAdd(ingredient, 1, out _);
            CraftingService service = new(new SequenceRandom());

            Assert.That(service.CanCraft(recipe, inventory, inventory), Is.True);
            Assert.That(
                service.TryCraft(recipe, inventory, inventory, out _), Is.True);
            Assert.That(
                inventory.GetCount(output, ItemData.Rarity.Common), Is.EqualTo(1));
        }

        [Test]
        public void InsufficientOutputSpaceDoesNotConsumeIngredients()
        {
            ItemData ingredient = CreateItem("ingredient", 10);
            ItemData blocker = CreateItem("blocker", 10);
            ItemData output = CreateItem("output", 1);
            CraftingRecipeData recipe = CreateRecipe(
                new[] { Exact(ingredient, 1) },
                Output(output, 2));
            Inventory inventory = new(2);
            inventory.TryAdd(ingredient, 2, out _);
            inventory.TryAdd(blocker, 1, out _);
            CraftingService service = new(new SequenceRandom());

            bool crafted = service.TryCraft(
                recipe, inventory, inventory, out CraftResult result);

            Assert.That(crafted, Is.False);
            Assert.That(
                result.FailureReason,
                Is.EqualTo(CraftFailureReason.InsufficientOutputSpace));
            Assert.That(
                inventory.GetCount(ingredient, ItemData.Rarity.Common), Is.EqualTo(2));
            Assert.That(
                inventory.GetCount(blocker, ItemData.Rarity.Common), Is.EqualTo(1));
        }

        [Test]
        public void ChanceOutputsUseIndependentRolls()
        {
            ItemData ingredient = CreateItem("ingredient", 10);
            ItemData output = CreateItem("output", 10);
            CraftingRecipeData recipe = CreateRecipe(
                new[] { Exact(ingredient, 1) },
                Output(output, 2, 0.5f, 3));
            Inventory inventory = new(2);
            inventory.TryAdd(ingredient, 1, out _);
            CraftingService service =
                new(new SequenceRandom(0.1f, 0.9f, 0.2f));

            service.TryCraft(recipe, inventory, inventory, out CraftResult result);

            Assert.That(
                inventory.GetCount(output, ItemData.Rarity.Common), Is.EqualTo(4));
            Assert.That(result.Outputs.Count, Is.EqualTo(1));
            Assert.That(result.Outputs[0].Count, Is.EqualTo(4));
        }

        [Test]
        public void MissingIngredientsLeaveInventoryUnchanged()
        {
            ItemData ingredient = CreateItem("ingredient", 10);
            ItemData output = CreateItem("output", 10);
            CraftingRecipeData recipe = CreateRecipe(
                new[] { Exact(ingredient, 3) },
                Output(output, 1));
            Inventory inventory = new(2);
            inventory.TryAdd(ingredient, 2, out _);

            bool crafted = new CraftingService(new SequenceRandom()).TryCraft(
                recipe, inventory, inventory, out CraftResult result);

            Assert.That(crafted, Is.False);
            Assert.That(
                result.FailureReason,
                Is.EqualTo(CraftFailureReason.MissingIngredients));
            Assert.That(
                inventory.GetCount(ingredient, ItemData.Rarity.Common), Is.EqualTo(2));
            Assert.That(
                inventory.GetCount(output, ItemData.Rarity.Common), Is.Zero);
        }

        private EntityTag CreateTag()
        {
            EntityTag tag = ScriptableObject.CreateInstance<EntityTag>();
            _objects.Add(tag);
            return tag;
        }

        private ItemData CreateItem(
            string id,
            int maxStack,
            params EntityTag[] tags)
        {
            ItemData item = ScriptableObject.CreateInstance<ItemData>();
            item.persistentId = id;
            item.maxStack = maxStack;
            typeof(ItemData)
                .GetField("tags", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(item, tags);
            _objects.Add(item);
            return item;
        }

        private CraftingRecipeData CreateRecipe(
            CraftingRecipeData.RecipeIngredient[] ingredients,
            params CraftingRecipeData.RecipeOutput[] outputs)
        {
            CraftingRecipeData recipe =
                ScriptableObject.CreateInstance<CraftingRecipeData>();
            recipe.ingredients = ingredients;
            recipe.output = outputs;
            _objects.Add(recipe);
            return recipe;
        }

        private static CraftingRecipeData.RecipeIngredient Exact(
            ItemData item,
            int count)
        {
            return new CraftingRecipeData.RecipeIngredient
            {
                matchType = CraftingRecipeData.IngredientMatchType.ExactItem,
                itemData = item,
                count = count
            };
        }

        private static CraftingRecipeData.RecipeIngredient Tagged(
            EntityTag tag,
            int count)
        {
            return new CraftingRecipeData.RecipeIngredient
            {
                matchType = CraftingRecipeData.IngredientMatchType.Tag,
                tag = tag,
                count = count
            };
        }

        private static CraftingRecipeData.RecipeOutput Output(
            ItemData item,
            int amount,
            float chance = 1,
            int rolls = 1)
        {
            return new CraftingRecipeData.RecipeOutput
            {
                itemData = item,
                amount = amount,
                chance = chance,
                rolls = rolls
            };
        }

        private sealed class SequenceRandom : ICraftingRandom
        {
            private readonly Queue<float> _values;

            public SequenceRandom(params float[] values)
            {
                _values = new Queue<float>(values);
            }

            public float Value()
            {
                return _values.Dequeue();
            }
        }
    }
}
#endif
