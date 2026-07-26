using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using Zenject;

namespace Project.Scripts
{
    public class ItemStackPickupPool :
        MonoMemoryPool<ItemData, int, ItemData.Rarity, ItemStackPickup>,
        IItemStackPickupPool
    {
        void IItemStackPickupPool.Spawn(
            ItemData itemData,
            int count,
            ItemData.Rarity rarity,
            UnityEngine.Vector3 position)
        {
            ItemStackPickup pickup = Spawn(itemData, count, rarity);
            pickup.transform.position = position;
        }

        void IItemStackPickupPool.Despawn(IItemStackPickup pickup)
        {
            if (pickup is ItemStackPickup itemStackPickup)
                Despawn(itemStackPickup);
        }

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
