using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;
using UnityEngine.Serialization;
using Zenject;

namespace Project.Scripts.Persistence
{
    [CreateAssetMenu(
        fileName = "Inventory Fuel Consumer Definition",
        menuName = "World/Components/Inventory Fuel Consumer")]
    public sealed class InventoryFuelConsumerDefinition : NodeComponentDefinition
    {
        [SerializeField] private ItemTag fuelTag;
        [FormerlySerializedAs("itemsPerOperation")]
        [SerializeField, Min(1)] private int fuelValuePerOperation = 1;

        public override void Install(
            GameObject host,
            DiContainer container,
            NodeComponentSpawnContext context)
        {
            if (host.GetComponent<PersistentInventory>() == null)
                container.InstantiateComponent<PersistentInventory>(host);

            InventoryFuelConsumer consumer =
                container.InstantiateComponent<InventoryFuelConsumer>(host);
            consumer.Initialize(fuelTag, fuelValuePerOperation);
        }
    }
}
