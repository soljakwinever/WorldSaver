using Project.Scripts.DataTypes;

namespace Project.Scripts.Interface
{
    public interface IItemStack : IToolTipData
    {
        ItemData Item { get; }
        ItemData.Rarity Rarity { get; }
        byte Durability { get; }
        int Capacity { get; }
        int RemainingCapacity { get; }
        bool IsFull { get; }
    }
}
