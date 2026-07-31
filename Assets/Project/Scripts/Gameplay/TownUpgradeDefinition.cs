using System;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    [CreateAssetMenu(
        fileName = "Town Upgrade",
        menuName = "World/Towns/Upgrade")]
    public sealed class TownUpgradeDefinition : ScriptableObject
    {
        [Serializable]
        public struct ItemCost
        {
            public ItemData item;
            [Min(1)] public int count;
            public ItemData.Rarity rarity;
        }

        [SerializeField] private ItemCost[] costs = Array.Empty<ItemCost>();
        [SerializeField, Min(0f)] private float townRadiusIncrease;
        [SerializeField, Min(0f)] private float resourceRadiusIncrease;
        [SerializeField, Min(0)] private int populationCapacityIncrease;
        [SerializeField, Min(0f)] private float manaCapacityIncrease;
        [SerializeField, Min(0)] private int maximumHealthIncrease;

        public ItemCost[] Costs => costs ?? Array.Empty<ItemCost>();
        public float TownRadiusIncrease => townRadiusIncrease;
        public float ResourceRadiusIncrease => resourceRadiusIncrease;
        public int PopulationCapacityIncrease => populationCapacityIncrease;
        public float ManaCapacityIncrease => manaCapacityIncrease;
        public int MaximumHealthIncrease => maximumHealthIncrease;
    }
}
