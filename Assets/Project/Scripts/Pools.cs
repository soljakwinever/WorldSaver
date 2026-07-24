using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Zenject;

namespace Project.Scripts
{
    public class ItemStackPickupPool :
        MonoMemoryPool<ItemData, int, ItemData.Rarity, ItemStackPickup>,
        IItemStackPickupPool
    {
        protected override void Reinitialize(ItemData itemData, int count, ItemData.Rarity rarity, ItemStackPickup item)
        {
            item.SetPool(this);
            item.Initialize(itemData, count, rarity);
        }

        protected override void OnDespawned(ItemStackPickup item)
        {
            item.SetPool(null);
            base.OnDespawned(item);
        }
    }
}
