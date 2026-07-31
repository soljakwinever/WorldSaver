using Project.Scripts.DataTypes.SaveData;

namespace Project.Scripts.Interface
{
    public interface IPersistentEntity
    {
        NodeId Id { get; }
        void RemoveFromWorld();
    }
}
