using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using Project.Scripts.Utility;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public sealed class ItemStack : IItemStack
    {
        public ItemData Item { get; }
        public ItemData.Rarity Rarity { get; }
        public byte Durability { get; private set; }
        public float Durability01 => Durability / (float)byte.MaxValue;
        public bool IsBroken => Durability == 0;
        public int Count { get; private set; }
        public int Capacity => Item.maxStack;
        public int RemainingCapacity => Capacity - Count;
        public bool IsFull => Count == Capacity;
        public string DisplayName => Item != null ? Item.name : string.Empty;
        public string Description => Item?.description ?? string.Empty;
        public Color Color => ItemRarityUtility.GetRarityColor(Rarity);
        public Sprite Sprite => Item != null ? Item.sprite : null;

        public ItemStack(
            ItemData item,
            int count,
            ItemData.Rarity rarity = ItemData.Rarity.Common,
            byte durability = byte.MaxValue)
        {
            ValidateItem(item);

            if (count <= 0 || count > item.maxStack)
                throw new ArgumentOutOfRangeException(nameof(count),
                    $"Count must be between 1 and {item.maxStack}.");
            if (!Enum.IsDefined(typeof(ItemData.Rarity), rarity))
                throw new ArgumentOutOfRangeException(nameof(rarity));

            Item = item;
            Rarity = rarity;
            Durability = durability;
            Count = count;
        }

        public void SetDurability(byte durability)
        {
            Durability = durability;
        }

        public byte ApplyDurabilityDamage(byte amount)
        {
            int applied = Math.Min(amount, Durability);
            Durability = (byte)(Durability - applied);
            return (byte)applied;
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
