using System;
using Project.Scripts.DataTypes;

namespace Project.Scripts.Interface
{
    public readonly struct InventoryChange
    {
        public ItemData Item { get; }
        public ItemData.Rarity Rarity { get; }
        public byte Durability { get; }
        public bool MatchesAnyDurability { get; }
        public int CountDelta { get; }
        public GeneratedItemData GeneratedData { get; }

        public InventoryChange(
            ItemData item,
            int countDelta,
            ItemData.Rarity rarity = ItemData.Rarity.Common,
            byte? durability = null,
            GeneratedItemData generatedData = null)
        {
            Item = item != null ? item : throw new ArgumentNullException(nameof(item));
            if (countDelta == 0)
                throw new ArgumentOutOfRangeException(
                    nameof(countDelta), "Inventory changes cannot be zero.");
            if (!Enum.IsDefined(typeof(ItemData.Rarity), rarity))
                throw new ArgumentOutOfRangeException(nameof(rarity));

            CountDelta = countDelta;
            Rarity = rarity;
            Durability = durability ?? byte.MaxValue;
            MatchesAnyDurability = !durability.HasValue;
            GeneratedData = generatedData;
        }
    }
}
