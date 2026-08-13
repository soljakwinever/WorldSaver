using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [Serializable]
    public sealed class HarvestableComponentData : ComponentDefinitionData
    {
        public InteractionType interactionType;
        public ToolType toolType;
        public ItemData itemData;
        public int minimumItemsSpawned;
        public int maximumItemsSpawned;
        public bool useRarity;
        public bool destroyOnPickup;
    }

    [CreateAssetMenu(fileName = "Harvestable Component Definition", menuName = "World/Components/Harvestable Component")]
    public class HarvestableComponentDefinition : NodeComponentDefinition
    {
        public override Type DataType =>
            typeof(HarvestableComponentData);
        
        protected override void InstallComponent(
            GameObject host,
            DiContainer container,
            NodeComponentSpawnContext context,
            ComponentDefinitionData data)
        {
            var configuration = (HarvestableComponentData)data;
            HarvestableObject harvestableObject = container.InstantiateComponent<HarvestableObject>(host);
            
            harvestableObject.Initialize(
                configuration.interactionType,
                configuration.toolType,
                configuration.itemData,
                configuration.minimumItemsSpawned,
                configuration.maximumItemsSpawned,
                configuration.useRarity,
                ItemData.Rarity.Common,
                configuration.destroyOnPickup);
        }
    }
}
