using Unity.Mathematics;
using UnityEngine;

namespace Project.Scripts.Entities
{
    /// <summary>Scene/subscene authoring for test and hand-authored villagers.</summary>
    public sealed class UnitAuthoring : MonoBehaviour
    {
        [Min(1)] public int maxHealth = 100;
        [Min(0f)] public float moveSpeed = 3f;
        [Min(0f)] public float workRate = 1f;
        [Min(0f)] public float hungerPerSecond = 0.002f;
        [Min(1)] public int inventorySize = 6;
        public VillagerRole role = VillagerRole.Generalist;
        public VillagerJobMask allowedJobs = VillagerJobMask.All;

    }

    public static class VillagerDefaults
    {
        public static VillagerStats CreateStats(
            int maxHealth = 100,
            float movementSpeed = 3f,
            float workRate = 1f,
            float hungerDrain = 0.002f,
            int inventorySize = 6) => new()
        {
            maxHealth = math.max(1, maxHealth),
            maxHunger = 100f,
            maxEnergy = 100f,
            maxMana = 100f,
            workRate = math.max(0f, workRate),
            hungerDrainPerSecond = math.max(0f, hungerDrain) * 100f,
            movementEnergyPerSecond = 0.5f,
            workEnergyPerSecond = 0.75f,
            restEnergyPerSecond = 8f,
            manaRegenerationPerSecond = 1f,
            healthRegenerationPerSecond = 0.25f,
            starvationDamagePerSecond = 1f,
            movementSpeed = math.max(0f, movementSpeed),
            criticalNeedFraction = 0.2f,
            inventorySize = math.max(1, inventorySize),
            attack = 5,
            defense = 0
        };

        public static VillagerNeeds CreateNeeds(int maxHealth = 100) => new()
        {
            hunger = 100f,
            energy = 100f,
            health = math.max(1, maxHealth),
            mana = 100f
        };
    }
}
