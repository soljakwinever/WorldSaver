using System;
using Project.Scripts.DataTypes;

namespace Project.Scripts.Interface
{
    public readonly struct InventoryChange
    {
        public ItemData Item { get; }
        public ItemData.Rarity Rarity { get; }
        public int CountDelta { get; }

        public InventoryChange(
            ItemData item,
            int countDelta,
            ItemData.Rarity rarity = ItemData.Rarity.Common)
        {
            Item = item != null ? item : throw new ArgumentNullException(nameof(item));
            if (countDelta == 0)
                throw new ArgumentOutOfRangeException(
                    nameof(countDelta), "Inventory changes cannot be zero.");
            if (!Enum.IsDefined(typeof(ItemData.Rarity), rarity))
                throw new ArgumentOutOfRangeException(nameof(rarity));

            CountDelta = countDelta;
            Rarity = rarity;
        }
    }
}
