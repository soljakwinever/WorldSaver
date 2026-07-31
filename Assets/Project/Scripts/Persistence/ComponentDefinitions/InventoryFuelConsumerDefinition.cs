using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [Serializable]
    public sealed class InventoryFuelConsumerData : ComponentDefinitionData
    {
        public EntityTag fuelTag;
        [Min(1)] public int fuelValuePerOperation = 1;
    }

    [CreateAssetMenu(
        fileName = "Inventory Fuel Consumer Definition",
        menuName = "World/Components/Inventory Fuel Consumer")]
    public sealed class InventoryFuelConsumerDefinition : NodeComponentDefinition
    {
        public override Type DataType =>
            typeof(InventoryFuelConsumerData);

        protected override void InstallComponent(
            GameObject host,
            DiContainer container,
            NodeComponentSpawnContext context,
            ComponentDefinitionData data)
        {
            var configuration = (InventoryFuelConsumerData)data;
            if (host.GetComponent<PersistentInventory>() == null)
                container.InstantiateComponent<PersistentInventory>(host);

            InventoryFuelConsumer consumer =
                container.InstantiateComponent<InventoryFuelConsumer>(host);
            consumer.Initialize(
                configuration.fuelTag,
                configuration.fuelValuePerOperation);
        }
    }
}
