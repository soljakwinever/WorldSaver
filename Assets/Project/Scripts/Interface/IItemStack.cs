using Project.Scripts.DataTypes;

namespace Project.Scripts.Interface
{
    public interface IItemStack
    {
        ItemData Item { get; }
        ItemData.Rarity Rarity { get; }
        int Count { get; }
        int Capacity { get; }
        int RemainingCapacity { get; }
        bool IsFull { get; }
    }
}
