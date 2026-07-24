using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [CreateAssetMenu(fileName = "Persistent Production Definition", menuName = "World/Persistence/Persistent Production", order = 0)]
    public class PersistentProductionDefinition : NodeComponentDefinition
    {
        [SerializeField] private ItemData itemData;
        [SerializeField, Min(1)] private int itemsPerCycle = 1;
        [SerializeField, Min(1)] private long ticksPerCycle = 600;
        [SerializeField] private bool generateRarity;
        [SerializeField] private ItemData.Rarity rarity = ItemData.Rarity.Common;
        [SerializeField] private EntityConditionDefinition activationCondition;
        
        public override void Install(GameObject host, DiContainer container, NodeComponentSpawnContext context)
        {
            PersistentInventory inventory = host.GetComponent<PersistentInventory>();
            if (inventory == null)
                inventory = container.InstantiateComponent<PersistentInventory>(host);

            PersistentProduction production =
                container.InstantiateComponent<PersistentProduction>(host);
            production.Initialize(
                itemData,
                itemsPerCycle,
                ticksPerCycle,
                generateRarity,
                rarity,
                activationCondition);
        }
    }
}
