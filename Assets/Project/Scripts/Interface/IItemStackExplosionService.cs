using System.Collections.Generic;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Interface
{
    public readonly struct ItemStackExplosionEntry
    {
        public ItemData Item { get; }
        public int Count { get; }
        public ItemData.Rarity Rarity { get; }
        public byte Durability { get; }

        public ItemStackExplosionEntry(
            ItemData item,
            int count,
            ItemData.Rarity rarity,
            byte durability = byte.MaxValue)
        {
            Item = item;
            Count = count;
            Rarity = rarity;
            Durability = durability;
        }
    }

    public interface IItemStackExplosionService
    {
        void Explode(
            IReadOnlyList<ItemStackExplosionEntry> stacks,
            Vector3 position);

        void Explode(
            IReadOnlyList<ItemStackExplosionEntry> stacks,
            Vector3 position,
            float impulse,
            float impulseVariation);
    }
}
