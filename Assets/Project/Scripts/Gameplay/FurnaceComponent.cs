using System;
using System.Collections.Generic;
using System.IO;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class FurnaceComponent : MonoBehaviour, IInteractable,
        IEntityComponent, IPersistentComponent, IOfflineSimulatable,
        IFurnaceStation
    {
        public const ushort TypeId = 11;
        private const ushort CurrentVersion = 3;
        public const int OutputSlotCount = 9;

        [SerializeField] private string windowTitle = "Furnace";
        [SerializeField] private string interactionPrompt = "Use furnace";
        [SerializeField] private EntityTag fuelTag;
        [SerializeField] private RecipeList recipeList;
        [SerializeField, Min(1)] private int ingredientSlots = 3;
        [SerializeField, Min(1)] private long ticksPerCycle = 600;
        [SerializeField, Min(1)] private int fuelValuePerCycle = 1;

        private Inventory _fuel = new(1);
        private Inventory _ingredients = new(3);
        private Inventory _output = new(OutputSlotCount);
        private ItemCatalog _catalog;
        private IWorldClock _clock;
        private IComponentWindowService _window;
        private ICraftingRandom _random;
        private long _lastTick;
        private long _progressTicks;
        private long _storedFuelUnits;
        private bool _tickInitialized;

        public IPersistentEntity PersistentEntity { get; set; }
        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => CurrentVersion;
        public string WindowTitle => windowTitle;
        public IInventory FuelInventory => _fuel;
        public IInventory IngredientInventory => _ingredients;
        public IInventory OutputInventory => _output;
        public float Progress01 => ticksPerCycle <= 0
            ? 0f
            : Mathf.Clamp01(_progressTicks / (float)ticksPerCycle);
        public bool IsBurning =>
            GetAvailableFuelUnits() >= RequiredFuelUnits &&
            TryGetReadyRecipe(out _);
        private long RequiredFuelUnits => checked(
            (long)fuelValuePerCycle * ItemData.FuelUnitsPerBaseValue);

        [Inject]
        public void Construct(
            IWorldClock clock,
            IComponentWindowService window,
            ItemCatalog catalog,
            ICraftingRandom random)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public void Initialize(
            string title,
            string prompt,
            EntityTag acceptedFuelTag,
            RecipeList recipes,
            int configuredIngredientSlots,
            long cycleTicks,
            int fuelCost)
        {
            windowTitle = string.IsNullOrWhiteSpace(title) ? "Furnace" : title;
            interactionPrompt = string.IsNullOrWhiteSpace(prompt)
                ? "Use furnace"
                : prompt;
            fuelTag = acceptedFuelTag;
            recipeList = recipes;
            ingredientSlots = Math.Max(1, configuredIngredientSlots);
            ticksPerCycle = Math.Max(1, cycleTicks);
            fuelValuePerCycle = Math.Max(1, fuelCost);
            _fuel = new Inventory(1);
            _ingredients = new Inventory(ingredientSlots);
            _output = new Inventory(OutputSlotCount);
            ValidateConfiguration();
        }

        private void Update()
        {
            if (_clock == null)
                return;

            long now = _clock.CurrentTick;
            if (!_tickInitialized)
            {
                _lastTick = now;
                _tickInitialized = true;
                return;
            }

            Simulate(_lastTick, now);
        }

        public void SimulateOffline(
            long fromTick,
            long toTick,
            OfflineSimulationPolicy policy)
        {
            if (policy == OfflineSimulationPolicy.None || toTick <= fromTick)
                return;

            long start = _tickInitialized ? _lastTick : fromTick;
            _tickInitialized = true;
            Simulate(start, toTick);
        }

        private void Simulate(long fromTick, long toTick)
        {
            if (toTick <= fromTick)
                return;

            ValidateConfiguration();
            long progressBeforeSimulation = _progressTicks;
            long totalProgress = checked(
                _progressTicks + (toTick - fromTick));
            long requestedCycles = totalProgress / ticksPerCycle;
            if (requestedCycles == 0)
            {
                if (IsBurning)
                    _progressTicks = totalProgress;
                _lastTick = toTick;
                return;
            }

            long completedCycles = 0;
            while (completedCycles < requestedCycles &&
                   GetAvailableFuelUnits() >= RequiredFuelUnits &&
                   TryGetReadyRecipe(out CraftingRecipeData recipe) &&
                   TryCraftCycle(recipe))
            {
                ConsumeFuel(RequiredFuelUnits);
                completedCycles++;
            }

            _progressTicks = completedCycles == requestedCycles
                ? totalProgress % ticksPerCycle
                : completedCycles == 0
                    ? progressBeforeSimulation
                    : 0;
            _lastTick = toTick;
        }

        public bool TryInsertFuel(IInventory source, IItemStack stack)
        {
            return TryTransferInto(
                source, _fuel, stack, IsValidFuel);
        }

        public bool TryInsertIngredient(IInventory source, IItemStack stack)
        {
            return TryTransferInto(
                source, _ingredients, stack, IsValidIngredient);
        }

        public bool TryCollectIngredient(
            IInventory destination,
            IItemStack stack)
        {
            return TryTransferOutOf(_ingredients, destination, stack);
        }

        public bool TryCollectOutput(
            IInventory destination,
            IItemStack stack)
        {
            return TryTransferOutOf(_output, destination, stack);
        }

        public int CollectAll(IInventory destination)
        {
            int collected = 0;
            while (_output.Stacks.Count > 0)
            {
                IItemStack stack = _output.Stacks[0];
                int count = stack.Count;
                if (!TryCollectOutput(destination, stack))
                    break;
                collected = checked(collected + count);
            }
            return collected;
        }

        public bool CanInteract(InteractionContext context) =>
            context.interactionType == InteractionType.Direct &&
            _window != null &&
            context.user != null &&
            context.user.GetComponentInParent<PersistentInventory>() != null;

        public void Interact(InteractionContext context)
        {
            if (!CanInteract(context))
                return;
            IInventory player =
                context.user.GetComponentInParent<PersistentInventory>();
            _window.Open(new ComponentWindowRequest(
                windowTitle,
                new Vector2(720f, 520f),
                new FurnaceWindowSection(this, player)));
        }

        public string GetInteractionPrompt(InteractionContext context) =>
            context.interactionType == InteractionType.Direct
                ? interactionPrompt
                : string.Empty;

        public Vector3 GetPosition() => transform.position;

        public void WriteState(BinaryWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            writer.Write(_lastTick);
            writer.Write(_progressTicks);
            writer.Write(_storedFuelUnits);
            WriteInventory(writer, _fuel);
            WriteInventory(writer, _ingredients);
            WriteInventory(writer, _output);
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            try
            {
                ReadStateCore(reader, savedVersion);
            }
            catch (InvalidDataException exception)
            {
                DiscardInvalidFurnace(exception);
            }
            catch (EndOfStreamException exception)
            {
                DiscardInvalidFurnace(exception);
            }
        }

        private void ReadStateCore(
            BinaryReader reader,
            ushort savedVersion)
        {
            if (savedVersion < 1 || savedVersion > CurrentVersion)
                throw new InvalidDataException(
                    $"Unsupported furnace state version {savedVersion}.");

            _lastTick = reader.ReadInt64();
            _progressTicks = reader.ReadInt64();
            _storedFuelUnits = reader.ReadInt64();
            if (_progressTicks < 0 || _progressTicks >= ticksPerCycle ||
                _storedFuelUnits < 0)
                throw new InvalidDataException(
                    "Furnace timing or fuel state is invalid.");
            _fuel = ReadInventory(
                reader, 1, IsValidFuel, savedVersion);
            _ingredients = savedVersion >= 2
                ? ReadInventory(
                    reader,
                    ingredientSlots,
                    IsValidIngredient,
                    savedVersion)
                : new Inventory(ingredientSlots);
            _output = ReadInventory(
                reader, OutputSlotCount, null, savedVersion);
            _tickInitialized = _lastTick != 0;
        }

        private void DiscardInvalidFurnace(
            Exception exception)
        {
            string entityLabel = PersistentEntity != null
                ? PersistentEntity.Id.ToString()
                : gameObject.name;
            Debug.LogWarning(
                $"Discarding furnace entity {entityLabel} because its " +
                $"saved state is incompatible with the current furnace " +
                $"configuration: {exception.Message}",
                this);

            if (PersistentEntity != null)
            {
                PersistentEntity.RemoveFromWorld();
            }

            // Restore normally supplies an owned PersistentEntity. Keeping this
            // fallback makes a malformed standalone/authored furnace harmless.
            gameObject.SetActive(false);
        }

        public bool IsAtBaseline() =>
            _lastTick == 0 && _progressTicks == 0 && _storedFuelUnits == 0 &&
            _fuel.OccupiedSlots == 0 &&
            _ingredients.OccupiedSlots == 0 &&
            _output.OccupiedSlots == 0;

        /// <summary>
        /// Selects an output rarity from the consumed ingredient rarities.
        /// Equal rarities are deterministic. For mixed inputs, each consumed
        /// unit is weighted by its rarity grade (Common=1 through Legendary=5),
        /// so the lowest ingredient is the floor and better ingredients have
        /// progressively greater odds.
        /// </summary>
        public static ItemData.Rarity DetermineOutputRarity(
            IReadOnlyList<IItemStack> consumedIngredients,
            float roll)
        {
            if (consumedIngredients == null ||
                consumedIngredients.Count == 0)
                throw new ArgumentException(
                    "At least one consumed ingredient is required.",
                    nameof(consumedIngredients));
            if (float.IsNaN(roll) || roll < 0f || roll > 1f)
                throw new ArgumentOutOfRangeException(nameof(roll));

            long totalWeight = 0;
            long[] weights = new long[
                Enum.GetValues(typeof(ItemData.Rarity)).Length];
            for (int i = 0; i < consumedIngredients.Count; i++)
            {
                IItemStack stack = consumedIngredients[i] ??
                    throw new ArgumentException(
                        "Consumed ingredients cannot contain null stacks.",
                        nameof(consumedIngredients));
                int rarity = (int)stack.Rarity;
                long weight = checked(
                    (long)stack.Count * (rarity + 1));
                weights[rarity] = checked(weights[rarity] + weight);
                totalWeight = checked(totalWeight + weight);
            }

            if (roll >= 1f)
            {
                for (int rarity = weights.Length - 1;
                     rarity >= 0;
                     rarity--)
                {
                    if (weights[rarity] > 0)
                        return (ItemData.Rarity)rarity;
                }
            }

            double target = roll * totalWeight;
            long cumulative = 0;
            for (int rarity = 0; rarity < weights.Length; rarity++)
            {
                cumulative += weights[rarity];
                if (target < cumulative)
                    return (ItemData.Rarity)rarity;
            }

            return ItemData.Rarity.Legendary;
        }

        private bool TryCraftCycle(CraftingRecipeData recipe)
        {
            if (!TryAllocateIngredients(
                    recipe, out List<IItemStack> consumed,
                    out List<InventoryChange> removals))
                return false;

            ItemData.Rarity rarity = DetermineOutputRarity(
                consumed, GetRandomValue());
            List<InventoryChange> maximumOutputs =
                BuildOutputChanges(recipe, rarity, rollChance: false);
            if (!_ingredients.CanApplyChanges(removals) ||
                !_output.CanApplyChanges(maximumOutputs))
                return false;

            List<InventoryChange> actualOutputs =
                BuildOutputChanges(recipe, rarity, rollChance: true);
            if (!_ingredients.TryApplyChanges(removals))
                return false;
            if (!_output.TryApplyChanges(actualOutputs))
                throw new InvalidOperationException(
                    "Furnace output capacity changed during simulation.");
            return true;
        }

        private bool TryGetReadyRecipe(out CraftingRecipeData recipe)
        {
            recipe = null;
            if (recipeList == null)
                return false;

            for (int i = 0; i < recipeList.Count; i++)
            {
                CraftingRecipeData candidate = recipeList[i];
                if (!TryAllocateIngredients(
                        candidate, out List<IItemStack> consumed,
                        out List<InventoryChange> removals))
                    continue;
                ItemData.Rarity lowest = GetLowestRarity(consumed);
                List<InventoryChange> maximumOutputs =
                    BuildOutputChanges(candidate, lowest, rollChance: false);
                if (_ingredients.CanApplyChanges(removals) &&
                    _output.CanApplyChanges(maximumOutputs))
                {
                    recipe = candidate;
                    return true;
                }
            }

            return false;
        }

        private bool TryAllocateIngredients(
            CraftingRecipeData recipe,
            out List<IItemStack> consumed,
            out List<InventoryChange> removals)
        {
            consumed = new List<IItemStack>();
            removals = new List<InventoryChange>();
            if (!IsValidRecipe(recipe))
                return false;

            List<AvailableStack> available = new();
            for (int i = 0; i < _ingredients.Stacks.Count; i++)
            {
                IItemStack stack = _ingredients.Stacks[i];
                available.Add(new AvailableStack(
                    stack.Item,
                    stack.Rarity,
                    stack.Durability,
                    stack.Count));
            }

            Dictionary<ItemRarityKey, int> allocated = new();
            for (int pass = 0; pass < 2; pass++)
            {
                CraftingRecipeData.IngredientMatchType type = pass == 0
                    ? CraftingRecipeData.IngredientMatchType.ExactItem
                    : CraftingRecipeData.IngredientMatchType.Tag;
                for (int i = 0; i < recipe.ingredients.Length; i++)
                {
                    CraftingRecipeData.RecipeIngredient ingredient =
                        recipe.ingredients[i];
                    if (ingredient.matchType != type)
                        continue;
                    if (!TryAllocateIngredient(
                            ingredient, available, allocated))
                        return false;
                }
            }

            foreach (KeyValuePair<ItemRarityKey, int> pair in allocated)
            {
                int remaining = pair.Value;
                while (remaining > 0)
                {
                    int count = Math.Min(
                        remaining, pair.Key.Item.maxStack);
                    consumed.Add(new ItemStack(
                        pair.Key.Item,
                        count,
                        pair.Key.Rarity,
                        pair.Key.Durability));
                    remaining -= count;
                }
                removals.Add(new InventoryChange(
                    pair.Key.Item,
                    -pair.Value,
                    pair.Key.Rarity,
                    pair.Key.Durability));
            }
            return true;
        }

        private static bool TryAllocateIngredient(
            CraftingRecipeData.RecipeIngredient ingredient,
            List<AvailableStack> available,
            Dictionary<ItemRarityKey, int> allocated)
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

                    int amount = Math.Min(remaining, stack.Remaining);
                    stack.Remaining -= amount;
                    remaining -= amount;
                    ItemRarityKey key =
                        new(
                            stack.Item,
                            stack.Rarity,
                            stack.Durability);
                    allocated.TryGetValue(key, out int previous);
                    allocated[key] = checked(previous + amount);
                }
            }
            return remaining == 0;
        }

        private List<InventoryChange> BuildOutputChanges(
            CraftingRecipeData recipe,
            ItemData.Rarity rarity,
            bool rollChance)
        {
            Dictionary<ItemData, int> totals = new();
            for (int i = 0; i < recipe.output.Length; i++)
            {
                CraftingRecipeData.RecipeOutput output = recipe.output[i];
                int successfulRolls = 0;
                for (int roll = 0; roll < output.rolls; roll++)
                {
                    if (!rollChance || output.chance >= 1f ||
                        (output.chance > 0f &&
                         GetRandomValue() < output.chance))
                        successfulRolls++;
                }
                if (successfulRolls == 0)
                    continue;
                int amount = checked(
                    output.amount * successfulRolls);
                totals.TryGetValue(output.itemData, out int previous);
                totals[output.itemData] = checked(previous + amount);
            }

            List<InventoryChange> changes = new(totals.Count);
            foreach (KeyValuePair<ItemData, int> pair in totals)
            {
                changes.Add(new InventoryChange(
                    pair.Key, pair.Value, rarity));
            }
            return changes;
        }

        private static ItemData.Rarity GetLowestRarity(
            IReadOnlyList<IItemStack> stacks)
        {
            ItemData.Rarity result = ItemData.Rarity.Legendary;
            for (int i = 0; i < stacks.Count; i++)
            {
                if ((int)stacks[i].Rarity < (int)result)
                    result = stacks[i].Rarity;
            }
            return result;
        }

        private bool IsValidIngredient(ItemData item)
        {
            if (item == null || recipeList == null)
                return false;
            for (int recipeIndex = 0;
                 recipeIndex < recipeList.Count;
                 recipeIndex++)
            {
                CraftingRecipeData recipe = recipeList[recipeIndex];
                if (recipe?.ingredients == null)
                    continue;
                for (int i = 0; i < recipe.ingredients.Length; i++)
                {
                    CraftingRecipeData.RecipeIngredient ingredient =
                        recipe.ingredients[i];
                    if (ingredient == null)
                        continue;
                    if (ingredient.matchType ==
                            CraftingRecipeData.IngredientMatchType.ExactItem
                        && ingredient.itemData == item)
                        return true;
                    if (ingredient.matchType ==
                            CraftingRecipeData.IngredientMatchType.Tag &&
                        ingredient.tag != null &&
                        item.HasTag(ingredient.tag))
                        return true;
                }
            }
            return false;
        }

        private static bool IsValidRecipe(CraftingRecipeData recipe)
        {
            if (recipe?.ingredients == null ||
                recipe.output == null ||
                recipe.ingredients.Length == 0 ||
                recipe.output.Length == 0)
                return false;
            for (int i = 0; i < recipe.ingredients.Length; i++)
            {
                CraftingRecipeData.RecipeIngredient ingredient =
                    recipe.ingredients[i];
                if (ingredient == null || ingredient.count < 1)
                    return false;
                bool exact = ingredient.matchType ==
                    CraftingRecipeData.IngredientMatchType.ExactItem;
                bool tagged = ingredient.matchType ==
                    CraftingRecipeData.IngredientMatchType.Tag;
                if ((!exact && !tagged) ||
                    (exact && ingredient.itemData == null) ||
                    (tagged && ingredient.tag == null))
                    return false;
            }
            for (int i = 0; i < recipe.output.Length; i++)
            {
                CraftingRecipeData.RecipeOutput output = recipe.output[i];
                if (output == null || output.itemData == null ||
                    output.itemData.maxStack < 1 ||
                    output.amount < 1 || output.rolls < 1 ||
                    output.chance < 0f || output.chance > 1f ||
                    float.IsNaN(output.chance))
                    return false;
            }
            return true;
        }

        private static bool TryTransferInto(
            IInventory source,
            IInventory destination,
            IItemStack stack,
            Predicate<ItemData> accepts)
        {
            if (source == null || destination == null || stack == null ||
                accepts == null || !accepts(stack.Item))
                return false;
            var addition = new[]
            {
                new InventoryChange(
                    stack.Item,
                    stack.Count,
                    stack.Rarity,
                    stack.Durability)
            };
            if (!destination.CanApplyChanges(addition) ||
                !source.TryRemove(stack))
                return false;
            if (destination.TryAdd(stack, out int remainder) &&
                remainder == 0)
                return true;
            source.TryAdd(stack, out _);
            return false;
        }

        private static bool TryTransferOutOf(
            IInventory source,
            IInventory destination,
            IItemStack stack)
        {
            if (source == null || destination == null || stack == null ||
                !source.Contains(stack))
                return false;
            var addition = new[]
            {
                new InventoryChange(
                    stack.Item,
                    stack.Count,
                    stack.Rarity,
                    stack.Durability)
            };
            if (!destination.CanApplyChanges(addition) ||
                !destination.TryApplyChanges(addition))
                return false;
            if (source.TryRemove(stack))
                return true;
            destination.TryApplyChanges(new[]
            {
                new InventoryChange(
                    stack.Item,
                    -stack.Count,
                    stack.Rarity,
                    stack.Durability)
            });
            return false;
        }

        private long GetAvailableFuelUnits()
        {
            long units = _storedFuelUnits;
            foreach (IItemStack stack in _fuel.Stacks)
            {
                units = checked(units + checked(
                    stack.Count *
                    stack.Item.GetFuelUnits(stack.Rarity)));
            }
            return units;
        }

        private void ConsumeFuel(long units)
        {
            while (_storedFuelUnits < units)
            {
                IItemStack stack = _fuel.Stacks[0];
                long value =
                    stack.Item.GetFuelUnits(stack.Rarity);
                _fuel.TryRemove(new ItemStack(
                    stack.Item,
                    1,
                    stack.Rarity,
                    stack.Durability));
                _storedFuelUnits =
                    checked(_storedFuelUnits + value);
            }
            _storedFuelUnits -= units;
        }

        private bool IsValidFuel(ItemData item) =>
            item != null && fuelTag != null &&
            item.HasTag(fuelTag) &&
            item.GetFuelUnits(ItemData.Rarity.Common) > 0;

        private float GetRandomValue()
        {
            return _random?.Value() ?? UnityEngine.Random.value;
        }

        private static void WriteInventory(
            BinaryWriter writer,
            IInventory inventory)
        {
            writer.Write(inventory.Stacks.Count);
            foreach (IItemStack stack in inventory.Stacks)
            {
                writer.Write(stack.Item.persistentId);
                writer.Write((byte)stack.Rarity);
                writer.Write(stack.Count);
                writer.Write(stack.Durability);
            }
        }

        private Inventory ReadInventory(
            BinaryReader reader,
            int size,
            Predicate<ItemData> accepts,
            ushort savedVersion)
        {
            if (_catalog == null)
                throw new InvalidOperationException(
                    "Furnace must be constructed before loading state.");
            int count = reader.ReadInt32();
            if (count < 0 || count > size)
                throw new InvalidDataException(
                    "Furnace inventory size is invalid.");
            Inventory inventory = new(size);
            for (int i = 0; i < count; i++)
            {
                string id = reader.ReadString();
                ItemData.Rarity rarity =
                    (ItemData.Rarity)reader.ReadByte();
                int amount = reader.ReadInt32();
                byte durability = savedVersion >= 3
                    ? reader.ReadByte()
                    : byte.MaxValue;
                if (!_catalog.TryGet(id, out ItemData item) ||
                    !Enum.IsDefined(
                        typeof(ItemData.Rarity), rarity) ||
                    amount <= 0 || amount > item.maxStack ||
                    (accepts != null && !accepts(item)))
                    throw new InvalidDataException(
                        $"Invalid furnace item stack '{id}'.");
                inventory.TryAdd(
                    item,
                    amount,
                    out int remainder,
                    rarity,
                    durability);
                if (remainder != 0)
                    throw new InvalidDataException(
                        "Furnace inventory exceeds capacity.");
            }
            return inventory;
        }

        private void ValidateConfiguration()
        {
            if (fuelTag == null || recipeList == null)
                throw new InvalidOperationException(
                    "Furnace requires a fuel tag and recipe list.");
            if (ingredientSlots < 1 || ticksPerCycle < 1 ||
                fuelValuePerCycle < 1)
                throw new InvalidOperationException(
                    "Furnace slot and cycle values must be positive.");
        }

        private sealed class AvailableStack
        {
            public ItemData Item { get; }
            public ItemData.Rarity Rarity { get; }
            public byte Durability { get; }
            public int Remaining { get; set; }

            public AvailableStack(
                ItemData item,
                ItemData.Rarity rarity,
                byte durability,
                int remaining)
            {
                Item = item;
                Rarity = rarity;
                Durability = durability;
                Remaining = remaining;
            }
        }

        private readonly struct ItemRarityKey :
            IEquatable<ItemRarityKey>
        {
            public ItemData Item { get; }
            public ItemData.Rarity Rarity { get; }
            public byte Durability { get; }

            public ItemRarityKey(
                ItemData item,
                ItemData.Rarity rarity,
                byte durability)
            {
                Item = item;
                Rarity = rarity;
                Durability = durability;
            }

            public bool Equals(ItemRarityKey other) =>
                Item == other.Item &&
                Rarity == other.Rarity &&
                Durability == other.Durability;

            public override bool Equals(object obj) =>
                obj is ItemRarityKey other && Equals(other);

            public override int GetHashCode() =>
                (((Item != null ? Item.GetHashCode() : 0) * 397) ^
                 (int)Rarity) * 397 ^ Durability;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ingredientSlots = Mathf.Max(1, ingredientSlots);
            ticksPerCycle = Math.Max(1, ticksPerCycle);
            fuelValuePerCycle = Mathf.Max(1, fuelValuePerCycle);
        }
#endif
    }
}
