using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;

namespace Project.Scripts.Gameplay
{
    public sealed class ItemStack : IItemStack
    {
        public ItemData Item { get; }
        public ItemData.Rarity Rarity { get; }
        public int Count { get; private set; }
        public int Capacity => Item.maxStack;
        public int RemainingCapacity => Capacity - Count;
        public bool IsFull => Count == Capacity;

        public ItemStack(ItemData item, int count, ItemData.Rarity rarity = ItemData.Rarity.Common)
        {
            ValidateItem(item);

            if (count <= 0 || count > item.maxStack)
                throw new ArgumentOutOfRangeException(nameof(count),
                    $"Count must be between 1 and {item.maxStack}.");
            if (!Enum.IsDefined(typeof(ItemData.Rarity), rarity))
                throw new ArgumentOutOfRangeException(nameof(rarity));

            Item = item;
            Rarity = rarity;
            Count = count;
        }

        internal int Add(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            int added = Math.Min(count, RemainingCapacity);
            Count += added;
            return count - added;
        }

        internal void Remove(int count)
        {
            if (count <= 0 || count > Count)
                throw new ArgumentOutOfRangeException(nameof(count));

            Count -= count;
        }

        internal static void ValidateItem(ItemData item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            if (item.maxStack <= 0)
                throw new ArgumentException("An item's maxStack must be greater than zero.", nameof(item));
        }
    }
}
