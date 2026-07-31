using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Interface
{
    public interface IItemStackPickupPool
    {
        void Spawn(
            ItemData item,
            int count,
            ItemData.Rarity rarity,
            Vector3 position,
            Vector2 impulse = default,
            byte durability = byte.MaxValue);

        void Despawn(IItemStackPickup pickup);
    }
}
