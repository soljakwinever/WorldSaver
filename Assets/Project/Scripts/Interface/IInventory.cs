using System.Collections.Generic;
using Project.Scripts.DataTypes;

namespace Project.Scripts.Interface
{
    public interface IInventory
    {
        int Size { get; }
        int OccupiedSlots { get; }
        IReadOnlyList<IItemStack> Stacks { get; }

        bool TryAdd(IItemStack stack, out int remainder);
        bool TryAdd(ItemData item, int count, out int remainder,
            ItemData.Rarity rarity = ItemData.Rarity.Common);
        bool TryRemove(IItemStack stack);
        bool TryRemove(ItemData item, int count, ItemData.Rarity rarity = ItemData.Rarity.Common);
        bool TryRemove(EntityTag tag, int count);
        bool TryRemoveOne(
            EntityTag tag,
            out ItemData item,
            out ItemData.Rarity rarity);
        int GetCount(ItemData item, ItemData.Rarity rarity = ItemData.Rarity.Common);
        int GetCount(EntityTag tag);
        bool Contains(IItemStack stack);
        bool Contains(ItemData item, int count = 1, ItemData.Rarity rarity = ItemData.Rarity.Common);
        bool Contains(EntityTag tag, int count = 1,
            ItemData.Rarity rarity = ItemData.Rarity.Common);
        bool CanApplyChanges(IReadOnlyList<InventoryChange> changes);
        bool TryApplyChanges(IReadOnlyList<InventoryChange> changes);
        void Clear();
    }
}
