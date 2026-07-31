using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [Serializable]
    public sealed class PersistentProductionData : ComponentDefinitionData
    {
        public ItemData itemData;
        [Min(1)] public int itemsPerCycle = 1;
        [Min(1)] public long ticksPerCycle = 600;
        public bool generateRarity;
        public ItemData.Rarity rarity = ItemData.Rarity.Common;
        public EntityConditionDefinition activationCondition;
    }

    [CreateAssetMenu(fileName = "Persistent Production Definition", menuName = "World/Persistence/Persistent Production", order = 0)]
    public class PersistentProductionDefinition : NodeComponentDefinition
    {
        public override Type DataType =>
            typeof(PersistentProductionData);
        
        protected override void InstallComponent(
            GameObject host,
            DiContainer container,
            NodeComponentSpawnContext context,
            ComponentDefinitionData data)
        {
            var configuration = (PersistentProductionData)data;
            PersistentInventory inventory = host.GetComponent<PersistentInventory>();
            if (inventory == null)
                inventory = container.InstantiateComponent<PersistentInventory>(host);

            PersistentProduction production =
                container.InstantiateComponent<PersistentProduction>(host);
            production.Initialize(
                configuration.itemData,
                configuration.itemsPerCycle,
                configuration.ticksPerCycle,
                configuration.generateRarity,
                configuration.rarity,
                configuration.activationCondition);
        }
    }
}
