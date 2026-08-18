using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using Zenject;

namespace Project.Scripts
{
    public class ItemStackPickupPool :
        MonoMemoryPool<
            ItemData,
            int,
            ItemData.Rarity,
            byte,
            ItemStackPickup>,
        IItemStackPickupPool
    {
        void IItemStackPickupPool.Spawn(
            ItemData itemData,
            int count,
            ItemData.Rarity rarity,
            UnityEngine.Vector3 position,
            UnityEngine.Vector2 impulse,
            byte durability,
            GeneratedItemData generatedData)
        {
            ItemStackPickup pickup =
                Spawn(itemData, count, rarity, durability);
            pickup.Initialize(itemData, count, rarity, durability, generatedData);
            pickup.transform.position = position;
            pickup.Launch(impulse);
        }

        void IItemStackPickupPool.Despawn(IItemStackPickup pickup)
        {
            if (pickup is ItemStackPickup itemStackPickup)
                Despawn(itemStackPickup);
        }

        protected override void Reinitialize(
            ItemData itemData,
            int count,
            ItemData.Rarity rarity,
            byte durability,
            ItemStackPickup item)
        {
            item.SetPool(this);
            item.Initialize(itemData, count, rarity, durability);
        }

        protected override void OnDespawned(ItemStackPickup item)
        {
            item.SetPool(null);
            base.OnDespawned(item);
        }
    }

    public sealed class WallDamageVisualPool :
        MonoMemoryPool<int, int, WallDamageVisual>
    {
        protected override void Reinitialize(
            int tileHealth,
            int maximumHealth,
            WallDamageVisual item)
        {
            item.SetHealth(tileHealth, maximumHealth);
        }

        protected override void OnDespawned(WallDamageVisual item)
        {
            item.Clear();
            base.OnDespawned(item);
        }
    }
}
