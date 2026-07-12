using Unity.Entities;
using Unity.Mathematics;

namespace Project.Scripts.Entities
{
    public struct UnitTag : IComponentData
    {
        
    }

    public struct UnitStats : IComponentData
    {
        public int maxHealth;
        public int maxEnergy;
        public float workRate;
        public float hungerRate;
        public float movementSpeed;
        public int inventorySize;
    }
    
    public struct UnitInventoryItem : IBufferElementData
    {
        public Entity ItemEntity;
        public int slotIndex;
        public int amount;
    }

    [InternalBufferCapacity(16)]
    public struct UnitInventoryBuffer : IBufferElementData
    {
        public int value;
        public static implicit operator int(UnitInventoryBuffer e) => e.value;
    }
    
    public struct UnitNeeds : IComponentData
    {
        public float food;
        public int health;
        public int energy;
    }

    public enum UnitMode : byte
    {
        Idle,
        MovingToTarget,
        Working,
        Attack,
        Eating,
        Sleep
    }

    public struct UnitState : IComponentData
    {
        public UnitMode value;
    }

    public struct UnitOrder : IComponentData
    {
        public Entity target;
        public float3 destination;
        public float workRemaining;
        public byte hasOrder;
    }

    public struct WorkSite : IComponentData
    {
        public float workRemaining;
        public int maximumWorkers;
        public int claimedWorkers;
    }
    
    public struct WorkPosition : IComponentData
    {
        public float3 value;
    }
}