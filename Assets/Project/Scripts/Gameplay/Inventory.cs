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
            ItemData.Rarity rarity = ItemData.Rarity.Common)
        {
            ItemStack.ValidateItem(item);
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater than zero.");

            remainder = count;

            for (int i = 0; i < _stacks.Count && remainder > 0; i++)
            {
                ItemStack stack = _stacks[i];
                if (stack.Item == item && stack.Rarity == rarity && !stack.IsFull)
                    remainder = stack.Add(remainder);
            }

            while (remainder > 0 && _stacks.Count < Size)
            {
                int stackCount = Math.Min(remainder, item.maxStack);
                AddStack(new ItemStack(item, stackCount, rarity));
                remainder -= stackCount;
            }

            return remainder == 0;
        }

        public bool TryAdd(IItemStack stack, out int remainder)
        {
            if (stack == null)
                throw new ArgumentNullException(nameof(stack));

            return TryAdd(stack.Item, stack.Count, out remainder, stack.Rarity);
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

            return TryRemove(stack.Item, stack.Count, stack.Rarity);
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

        public bool Contains(ItemData item, int count = 1,
            ItemData.Rarity rarity = ItemData.Rarity.Common)
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater than zero.");

            return GetCount(item, rarity) >= count;
        }

        public bool Contains(IItemStack stack)
        {
            if (stack == null)
                throw new ArgumentNullException(nameof(stack));

            return Contains(stack.Item, stack.Count, stack.Rarity);
        }

        public void Clear()
        {
            _stacks.Clear();
            _stackView.Clear();
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
    }
}
