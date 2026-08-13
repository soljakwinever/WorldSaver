using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Project.Scripts.Entities
{
    public struct VillagerTag : IComponentData { }
    public struct TownJobTag : IComponentData { }

    public struct VillagerIdentity : IComponentData
    {
        public FixedString64Bytes villagerId;
        public FixedString64Bytes townId;
    }

    public struct VillagerStats : IComponentData
    {
        public int maxHealth;
        public float maxHunger;
        public float maxEnergy;
        public float maxMana;
        public float workRate;
        public float hungerDrainPerSecond;
        public float movementEnergyPerSecond;
        public float workEnergyPerSecond;
        public float restEnergyPerSecond;
        public float manaRegenerationPerSecond;
        public float healthRegenerationPerSecond;
        public float starvationDamagePerSecond;
        public float movementSpeed;
        public float criticalNeedFraction;
        public int inventorySize;
        public int attack;
        public int defense;
    }

    public struct VillagerNeeds : IComponentData
    {
        public float hunger;
        public float energy;
        public int health;
        public float mana;
        public float pendingHealthDelta;
    }

    public struct VillagerAssignment : IComponentData
    {
        public VillagerRole role;
        public VillagerJobMask allowedJobs;
    }

    public struct VillagerState : IComponentData
    {
        public VillagerMode mode;
        public VillagerWorkPhase phase;
        public long activeJobId;
        public Entity activeJobEntity;
    }

    public struct VillagerOrder : IComponentData
    {
        public float3 destination;
        public float workRemaining;
        public byte hasOrder;
    }

    public struct TownJob : IComponentData
    {
        public long jobId;
        public FixedString64Bytes townId;
        public VillagerJobType type;
        public TownJobPriority priority;
        public TownJobStatus status;
        public float3 targetPosition;
        public float workRequired;
        public Entity claimedBy;
        public int targetHandle;
        public byte retryCount;
        public FixedString64Bytes constructionId;
    }

    public struct VillagerInventoryItem : IBufferElementData
    {
        public FixedString64Bytes itemId;
        public int amount;
        public byte rarity;
        public byte durability;
    }

    public struct VillagerEquippedItem : IBufferElementData
    {
        public FixedString64Bytes itemId;
        public byte slot;
        public byte rarity;
        public byte durability;
    }

    [InternalBufferCapacity(16)]
    public struct VillagerWaypoint : IBufferElementData
    {
        public float3 value;
    }

    public struct VillagerPathState : IComponentData
    {
        public int waypointIndex;
        public byte requestPending;
        public byte pathFailed;
    }
}
