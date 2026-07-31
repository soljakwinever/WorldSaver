using Project.Scripts.DataTypes;

namespace Project.Scripts.Interface
{
    public interface IItemStackPickup
    {
        public void SetPool(IItemStackPickupPool pool);
        public void Initialize(
            ItemData item,
            int count,
            ItemData.Rarity rarity,
            byte durability = byte.MaxValue);
    }
}
