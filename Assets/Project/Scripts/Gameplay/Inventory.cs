using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;

namespace Project.Scripts.Gameplay
{
    public sealed class Inventory : IInventory
    {
        private readonly List<ItemStack> _stacks = new();
        private readonly List<IItemStack> _stackView = new();
        private readonly IReadOnlyList<IItemStack> _readOnlyStacks;

        public int Size { get; }
        public int OccupiedSlots => _stacks.Count;
        public IReadOnlyList<IItemStack> Stacks => _readOnlyStacks;

        public Inventory(int size)
        {
            if (size <= 0)
                throw new ArgumentOutOfRangeException(nameof(size), "Inventory size must be greater than zero.");

            Size = size;
            _readOnlyStacks = _stackView.AsReadOnly();
        }

        public bool TryAdd(ItemData item, int count, out int remainder,
            ItemData.Rarity rarity = ItemData.Rarity.Common,
            byte durability = byte.MaxValue)
        {
            ItemStack.ValidateItem(item);
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater than zero.");

            remainder = count;

            for (int i = 0; i < _stacks.Count && remainder > 0; i++)
            {
                ItemStack stack = _stacks[i];
                if (stack.Item == item &&
                    stack.Rarity == rarity &&
                    stack.Durability == durability &&
                    !stack.IsFull)
                    remainder = stack.Add(remainder);
            }

            while (remainder > 0 && _stacks.Count < Size)
            {
                int stackCount = Math.Min(remainder, item.maxStack);
                AddStack(new ItemStack(
                    item, stackCount, rarity, durability));
                remainder -= stackCount;
            }

            return remainder == 0;
        }

        public bool TryAdd(IItemStack stack, out int remainder)
        {
            if (stack == null)
                throw new ArgumentNullException(nameof(stack));

            if (stack.GeneratedData == null)
                return TryAdd(stack.Item, stack.Count, out remainder, stack.Rarity, stack.Durability);
            if (stack.Count != 1 || _stacks.Count >= Size) { remainder = stack.Count; return false; }
            AddStack(new ItemStack(stack.Item, 1, stack.Rarity, stack.Durability, stack.GeneratedData));
            remainder = 0;
            return true;
        }

        public bool TryRemove(ItemData item, int count, ItemData.Rarity rarity)
        {
            ItemStack.ValidateItem(item);
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater than zero.");
            if (GetCount(item, rarity) < count)
                return false;

            int remaining = count;
            for (int i = _stacks.Count - 1; i >= 0 && remaining > 0; i--)
            {
                ItemStack stack = _stacks[i];
                if (stack.Item != item || stack.Rarity != rarity)
                    continue;

                int removed = Math.Min(stack.Count, remaining);
                stack.Remove(removed);
                remaining -= removed;

                if (stack.Count == 0)
                    RemoveStackAt(i);
            }

            return true;
        }

        public bool TryRemove(IItemStack stack)
        {
            if (stack == null)
                throw new ArgumentNullException(nameof(stack));

            if (GetCount(
                    stack.Item,
                    stack.Rarity,
                    stack.Durability) < stack.Count)
                return false;

            int remaining = stack.Count;
            for (int i = _stacks.Count - 1;
                 i >= 0 && remaining > 0;
                 i--)
            {
                ItemStack candidate = _stacks[i];
                if (candidate.Item != stack.Item ||
                    candidate.Rarity != stack.Rarity ||
                    candidate.Durability != stack.Durability ||
                    !Equals(candidate.GeneratedData, stack.GeneratedData))
                    continue;

                int removed = Math.Min(candidate.Count, remaining);
                candidate.Remove(removed);
                remaining -= removed;
                if (candidate.Count == 0)
                    RemoveStackAt(i);
            }

            return true;
        }

        public bool TryRemove(EntityTag tag, int count)
        {
            if (tag == null)
                throw new ArgumentNullException(nameof(tag));
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater than zero.");
            if (GetCount(tag) < count)
                return false;

            int remaining = count;
            for (int i = _stacks.Count - 1; i >= 0 && remaining > 0; i--)
            {
                ItemStack stack = _stacks[i];
                if (!stack.Item.HasTag(tag))
                    continue;

                int removed = Math.Min(stack.Count, remaining);
                stack.Remove(removed);
                remaining -= removed;

                if (stack.Count == 0)
                    RemoveStackAt(i);
            }

            return true;
        }

        public bool TryRemoveOne(
            EntityTag tag,
            out ItemData item,
            out ItemData.Rarity rarity)
        {
            if (tag == null)
                throw new ArgumentNullException(nameof(tag));

            for (int i = _stacks.Count - 1; i >= 0; i--)
            {
                ItemStack stack = _stacks[i];
                if (!stack.Item.HasTag(tag))
                    continue;

                item = stack.Item;
                rarity = stack.Rarity;
                stack.Remove(1);
                if (stack.Count == 0)
                    RemoveStackAt(i);
                return true;
            }

            item = null;
            rarity = default;
            return false;
        }

        public int GetCount(ItemData item, ItemData.Rarity rarity)
        {
            if (item == null)
                return 0;

            int total = 0;
            for (int i = 0; i < _stacks.Count; i++)
            {
                ItemStack stack = _stacks[i];
                if (stack.Item == item && stack.Rarity == rarity)
                    total += stack.Count;
            }

            return total;
        }

        public int GetCount(EntityTag tag)
        {
            if (tag == null)
                throw new ArgumentNullException(nameof(tag));

            int total = 0;
            for (int i = 0; i < _stacks.Count; i++)
            {
                ItemStack stack = _stacks[i];
                if (stack.Item.HasTag(tag))
                    total = checked(total + stack.Count);
            }

            return total;
        }

        public bool Contains(ItemData item, int count = 1,
            ItemData.Rarity rarity = ItemData.Rarity.Common)
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater than zero.");

            return GetCount(item, rarity) >= count;
        }

        public bool Contains(EntityTag tag, int count = 1,
            ItemData.Rarity rarity = ItemData.Rarity.Common)
        {
            if (tag == null)
                throw new ArgumentNullException(nameof(tag));
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater than zero.");
            if (!Enum.IsDefined(typeof(ItemData.Rarity), rarity))
                throw new ArgumentOutOfRangeException(nameof(rarity), rarity, "Unknown item rarity.");

            int total = 0;
            for (int i = 0; i < _stacks.Count; i++)
            {
                ItemStack stack = _stacks[i];
                if (!stack.Item.HasTag(tag) || stack.Rarity != rarity)
                    continue;

                total = checked(total + stack.Count);
                if (total >= count)
                    return true;
            }

            return false;
        }

        public bool Contains(IItemStack stack)
        {
            if (stack == null)
                throw new ArgumentNullException(nameof(stack));

            return GetCount(
                stack.Item,
                stack.Rarity,
                stack.Durability,
                stack.GeneratedData) >= stack.Count;
        }

        public bool CanApplyChanges(IReadOnlyList<InventoryChange> changes)
        {
            return TryBuildChangedStacks(changes, out _);
        }

        public bool TryApplyChanges(IReadOnlyList<InventoryChange> changes)
        {
            if (!TryBuildChangedStacks(changes, out List<ItemStack> changedStacks))
                return false;

            _stacks.Clear();
            _stackView.Clear();
            for (int i = 0; i < changedStacks.Count; i++)
                AddStack(changedStacks[i]);
            return true;
        }

        public void Clear()
        {
            _stacks.Clear();
            _stackView.Clear();
        }

        private bool TryBuildChangedStacks(
            IReadOnlyList<InventoryChange> changes,
            out List<ItemStack> changedStacks)
        {
            if (changes == null)
                throw new ArgumentNullException(nameof(changes));

            changedStacks = new List<ItemStack>(_stacks.Count);
            for (int i = 0; i < _stacks.Count; i++)
            {
                ItemStack stack = _stacks[i];
                changedStacks.Add(new ItemStack(
                    stack.Item,
                    stack.Count,
                    stack.Rarity,
                    stack.Durability,
                    stack.GeneratedData));
            }

            for (int changeIndex = 0; changeIndex < changes.Count; changeIndex++)
            {
                InventoryChange change = changes[changeIndex];
                ItemStack.ValidateItem(change.Item);

                int remaining = change.CountDelta;
                if (remaining < 0)
                {
                    remaining = checked(-remaining);
                    for (int i = changedStacks.Count - 1; i >= 0 && remaining > 0; i--)
                    {
                        ItemStack stack = changedStacks[i];
                        if (stack.Item != change.Item ||
                            stack.Rarity != change.Rarity ||
                            (!change.MatchesAnyDurability &&
                             stack.Durability != change.Durability) ||
                            (change.GeneratedData != null &&
                             !Equals(stack.GeneratedData, change.GeneratedData)))
                            continue;

                        int removed = Math.Min(stack.Count, remaining);
                        stack.Remove(removed);
                        remaining -= removed;
                        if (stack.Count == 0)
                            changedStacks.RemoveAt(i);
                    }

                    if (remaining > 0)
                        return false;
                    continue;
                }

                for (int i = 0; i < changedStacks.Count && remaining > 0; i++)
                {
                    ItemStack stack = changedStacks[i];
                    if (stack.Item == change.Item &&
                        stack.Rarity == change.Rarity &&
                        stack.Durability == change.Durability &&
                        Equals(stack.GeneratedData, change.GeneratedData) &&
                        !stack.IsFull)
                    {
                        remaining = stack.Add(remaining);
                    }
                }

                while (remaining > 0 && changedStacks.Count < Size)
                {
                    int stackCount = Math.Min(remaining, change.Item.maxStack);
                    changedStacks.Add(
                        new ItemStack(
                            change.Item,
                            stackCount,
                            change.Rarity,
                            change.Durability,
                            change.GeneratedData));
                    remaining -= stackCount;
                }

                if (remaining > 0)
                    return false;
            }

            return true;
        }

        private void AddStack(ItemStack stack)
        {
            _stacks.Add(stack);
            _stackView.Add(stack);
        }

        private void RemoveStackAt(int index)
        {
            _stacks.RemoveAt(index);
            _stackView.RemoveAt(index);
        }

        private int GetCount(
            ItemData item,
            ItemData.Rarity rarity,
            byte durability)
            => GetCount(item, rarity, durability, null);

        private int GetCount(
            ItemData item,
            ItemData.Rarity rarity,
            byte durability,
            GeneratedItemData generatedData)
        {
            int total = 0;
            for (int i = 0; i < _stacks.Count; i++)
            {
                ItemStack stack = _stacks[i];
                if (stack.Item == item &&
                    stack.Rarity == rarity &&
                    stack.Durability == durability &&
                    (generatedData == null || Equals(stack.GeneratedData, generatedData)))
                    total = checked(total + stack.Count);
            }

            return total;
        }
    }
}
