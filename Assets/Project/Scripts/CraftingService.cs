using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    public sealed class CraftingService : ICraftingService
    {
        private static readonly IReadOnlyList<IItemStack> NoOutputs =
            Array.Empty<IItemStack>();

        private readonly ICraftingRandom _random;

        public CraftingService() : this(new UnityCraftingRandom())
        {
        }

        [Inject]
        public CraftingService(ICraftingRandom random)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public bool CanCraft(
            CraftingRecipeData recipe,
            IInventory source,
            IInventory destination)
        {
            ValidateInventories(source, destination);
            return TryBuildPlan(
                recipe,
                source,
                destination,
                rollChanceOutputs: false,
                out _,
                out _);
        }

        public bool TryCraft(
            CraftingRecipeData recipe,
            IInventory source,
            IInventory destination,
            out CraftResult result)
        {
            ValidateInventories(source, destination);
            if (!TryBuildPlan(
                    recipe,
                    source,
                    destination,
                    rollChanceOutputs: true,
                    out CraftPlan plan,
                    out CraftFailureReason failureReason))
            {
                result = new CraftResult(false, failureReason, NoOutputs);
                return false;
            }

            if (ReferenceEquals(source, destination))
            {
                if (!source.TryApplyChanges(plan.CombinedChanges))
                {
                    result = new CraftResult(
                        false, CraftFailureReason.InsufficientOutputSpace, NoOutputs);
                    return false;
                }
            }
            else
            {
                // Both batches were validated against their complete post-craft states.
                // Inventory implementations commit a validated batch synchronously.
                if (!source.TryApplyChanges(plan.SourceChanges))
                {
                    result = new CraftResult(
                        false, CraftFailureReason.MissingIngredients, NoOutputs);
                    return false;
                }

                if (!destination.TryApplyChanges(plan.OutputChanges))
                    throw new InvalidOperationException(
                        "The destination inventory changed during a crafting transaction.");
            }

            result = new CraftResult(true, CraftFailureReason.None, plan.OutputStacks);
            return true;
        }

        private bool TryBuildPlan(
            CraftingRecipeData recipe,
            IInventory source,
            IInventory destination,
            bool rollChanceOutputs,
            out CraftPlan plan,
            out CraftFailureReason failureReason)
        {
            plan = null;
            if (!IsValidRecipe(recipe))
            {
                failureReason = CraftFailureReason.InvalidRecipe;
                return false;
            }

            List<AvailableStack> available = CreateAvailableStacks(source);
            Dictionary<ItemRarityKey, int> removals = new();

            // Exact requirements reserve their items before broader tag requirements.
            for (int pass = 0; pass < 2; pass++)
            {
                CraftingRecipeData.IngredientMatchType requiredType =
                    pass == 0
                        ? CraftingRecipeData.IngredientMatchType.ExactItem
                        : CraftingRecipeData.IngredientMatchType.Tag;

                for (int i = 0; i < recipe.ingredients.Length; i++)
                {
                    CraftingRecipeData.RecipeIngredient ingredient = recipe.ingredients[i];
                    if (ingredient.matchType != requiredType ||
                        !TryAllocateIngredient(ingredient, available, removals))
                    {
                        if (ingredient.matchType == requiredType)
                        {
                            failureReason = CraftFailureReason.MissingIngredients;
                            return false;
                        }
                    }
                }
            }

            Dictionary<ItemRarityKey, int> outputs =
                BuildOutputs(recipe, rollChanceOutputs);
            List<InventoryChange> sourceChanges = ToChanges(removals, -1);
            List<InventoryChange> outputChanges = ToChanges(outputs, 1);
            List<InventoryChange> combinedChanges =
                new(sourceChanges.Count + outputChanges.Count);
            combinedChanges.AddRange(sourceChanges);
            combinedChanges.AddRange(outputChanges);

            bool sameInventory = ReferenceEquals(source, destination);
            bool sourceValid = sameInventory
                ? source.CanApplyChanges(combinedChanges)
                : source.CanApplyChanges(sourceChanges);
            if (!sourceValid)
            {
                failureReason = sameInventory
                    ? CraftFailureReason.InsufficientOutputSpace
                    : CraftFailureReason.MissingIngredients;
                return false;
            }

            if (!sameInventory && !destination.CanApplyChanges(outputChanges))
            {
                failureReason = CraftFailureReason.InsufficientOutputSpace;
                return false;
            }

            plan = new CraftPlan(
                sourceChanges,
                outputChanges,
                combinedChanges,
                CreateOutputStacks(outputs));
            failureReason = CraftFailureReason.None;
            return true;
        }

        private static bool TryAllocateIngredient(
            CraftingRecipeData.RecipeIngredient ingredient,
            List<AvailableStack> available,
            Dictionary<ItemRarityKey, int> removals)
        {
            int remaining = ingredient.count;
            for (int rarity = (int)ItemData.Rarity.Common;
                 rarity <= (int)ItemData.Rarity.Legendary && remaining > 0;
                 rarity++)
            {
                for (int i = 0; i < available.Count && remaining > 0; i++)
                {
                    AvailableStack stack = available[i];
                    if ((int)stack.Rarity != rarity || stack.Remaining == 0)
                        continue;

                    bool matches = ingredient.matchType ==
                        CraftingRecipeData.IngredientMatchType.ExactItem
                            ? stack.Item == ingredient.itemData
                            : stack.Item.HasTag(ingredient.tag);
                    if (!matches)
                        continue;

                    int allocated = Math.Min(stack.Remaining, remaining);
                    stack.Remaining -= allocated;
                    remaining -= allocated;

                    ItemRarityKey key = new(stack.Item, stack.Rarity);
                    removals.TryGetValue(key, out int previous);
                    removals[key] = checked(previous + allocated);
                }
            }

            return remaining == 0;
        }

        private Dictionary<ItemRarityKey, int> BuildOutputs(
            CraftingRecipeData recipe,
            bool rollChanceOutputs)
        {
            Dictionary<ItemRarityKey, int> outputs = new();
            for (int i = 0; i < recipe.output.Length; i++)
            {
                CraftingRecipeData.RecipeOutput recipeOutput = recipe.output[i];
                int successfulRolls = 0;
                for (int roll = 0; roll < recipeOutput.rolls; roll++)
                {
                    if (!rollChanceOutputs ||
                        recipeOutput.chance >= 1f ||
                        (recipeOutput.chance > 0f && _random.Value() < recipeOutput.chance))
                    {
                        successfulRolls++;
                    }
                }

                if (successfulRolls == 0)
                    continue;

                int count = checked(recipeOutput.amount * successfulRolls);
                ItemRarityKey key =
                    new(recipeOutput.itemData, ItemData.Rarity.Common);
                outputs.TryGetValue(key, out int previous);
                outputs[key] = checked(previous + count);
            }

            return outputs;
        }

        private static bool IsValidRecipe(CraftingRecipeData recipe)
        {
            if (recipe == null ||
                recipe.ingredients == null ||
                recipe.output == null ||
                recipe.ingredients.Length == 0 ||
                recipe.output.Length == 0 ||
                recipe.workRequired <= 0 ||
                float.IsNaN(recipe.workRequired) ||
                float.IsInfinity(recipe.workRequired))
            {
                return false;
            }

            for (int i = 0; i < recipe.ingredients.Length; i++)
            {
                CraftingRecipeData.RecipeIngredient ingredient = recipe.ingredients[i];
                if (ingredient == null || ingredient.count < 1)
                    return false;

                bool exact = ingredient.matchType ==
                    CraftingRecipeData.IngredientMatchType.ExactItem;
                bool tagged = ingredient.matchType ==
                    CraftingRecipeData.IngredientMatchType.Tag;
                if ((!exact && !tagged) ||
                    (exact && (ingredient.itemData == null || ingredient.tag != null)) ||
                    (tagged && (ingredient.tag == null || ingredient.itemData != null)))
                {
                    return false;
                }
            }

            for (int i = 0; i < recipe.output.Length; i++)
            {
                CraftingRecipeData.RecipeOutput output = recipe.output[i];
                if (output == null ||
                    output.itemData == null ||
                    output.itemData.maxStack < 1 ||
                    output.amount < 1 ||
                    output.rolls < 1 ||
                    output.chance < 0 ||
                    output.chance > 1 ||
                    float.IsNaN(output.chance))
                {
                    return false;
                }
            }

            return true;
        }

        private static List<AvailableStack> CreateAvailableStacks(IInventory inventory)
        {
            List<AvailableStack> result = new(inventory.Stacks.Count);
            for (int i = 0; i < inventory.Stacks.Count; i++)
            {
                IItemStack stack = inventory.Stacks[i];
                result.Add(new AvailableStack(stack.Item, stack.Rarity, stack.Count));
            }

            return result;
        }

        private static List<InventoryChange> ToChanges(
            Dictionary<ItemRarityKey, int> amounts,
            int sign)
        {
            List<InventoryChange> result = new(amounts.Count);
            foreach (KeyValuePair<ItemRarityKey, int> pair in amounts)
            {
                result.Add(new InventoryChange(
                    pair.Key.Item,
                    checked(pair.Value * sign),
                    pair.Key.Rarity));
            }

            return result;
        }

        private static IReadOnlyList<IItemStack> CreateOutputStacks(
            Dictionary<ItemRarityKey, int> outputs)
        {
            List<IItemStack> result = new();
            foreach (KeyValuePair<ItemRarityKey, int> pair in outputs)
            {
                int remaining = pair.Value;
                while (remaining > 0)
                {
                    int count = Math.Min(remaining, pair.Key.Item.maxStack);
                    result.Add(new ItemStack(pair.Key.Item, count, pair.Key.Rarity));
                    remaining -= count;
                }
            }

            return result.AsReadOnly();
        }

        private static void ValidateInventories(
            IInventory source,
            IInventory destination)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
        }

        public sealed class UnityCraftingRandom : ICraftingRandom
        {
            public float Value()
            {
                return UnityEngine.Random.value;
            }
        }

        private sealed class AvailableStack
        {
            public ItemData Item { get; }
            public ItemData.Rarity Rarity { get; }
            public int Remaining { get; set; }

            public AvailableStack(
                ItemData item,
                ItemData.Rarity rarity,
                int remaining)
            {
                Item = item;
                Rarity = rarity;
                Remaining = remaining;
            }
        }

        private readonly struct ItemRarityKey : IEquatable<ItemRarityKey>
        {
            public ItemData Item { get; }
            public ItemData.Rarity Rarity { get; }

            public ItemRarityKey(ItemData item, ItemData.Rarity rarity)
            {
                Item = item;
                Rarity = rarity;
            }

            public bool Equals(ItemRarityKey other)
            {
                return Item == other.Item && Rarity == other.Rarity;
            }

            public override bool Equals(object obj)
            {
                return obj is ItemRarityKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return ((Item != null ? Item.GetHashCode() : 0) * 397) ^
                       (int)Rarity;
            }
        }

        private sealed class CraftPlan
        {
            public IReadOnlyList<InventoryChange> SourceChanges { get; }
            public IReadOnlyList<InventoryChange> OutputChanges { get; }
            public IReadOnlyList<InventoryChange> CombinedChanges { get; }
            public IReadOnlyList<IItemStack> OutputStacks { get; }

            public CraftPlan(
                IReadOnlyList<InventoryChange> sourceChanges,
                IReadOnlyList<InventoryChange> outputChanges,
                IReadOnlyList<InventoryChange> combinedChanges,
                IReadOnlyList<IItemStack> outputStacks)
            {
                SourceChanges = sourceChanges;
                OutputChanges = outputChanges;
                CombinedChanges = combinedChanges;
                OutputStacks = outputStacks;
            }
        }
    }
}
