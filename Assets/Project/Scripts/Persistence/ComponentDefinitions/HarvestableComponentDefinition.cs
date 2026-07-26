using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [CreateAssetMenu(fileName = "Harvestable Component Definition", menuName = "World/Components/Harvestable Component")]
    public class HarvestableComponentDefinition : NodeComponentDefinition
    {
        public InteractionType interactionType;
        public ToolType toolType;
        public ItemData itemData;
        public int minimumItemsSpawned;
        public int maximumItemsSpawned;
        public bool useRarity;
        
        public bool destroyOnPickup;
        
        public override void Install(GameObject host, DiContainer container, NodeComponentSpawnContext context)
        {
            HarvestableObject harvestableObject = container.InstantiateComponent<HarvestableObject>(host);
            
            harvestableObject.Initialize(interactionType, toolType, itemData, minimumItemsSpawned, maximumItemsSpawned);
        }
    }
}