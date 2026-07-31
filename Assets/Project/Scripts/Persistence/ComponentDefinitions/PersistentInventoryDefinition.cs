using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [Serializable]
    public sealed class PersistentInventoryData : ComponentDefinitionData
    {
        [Min(1)] public int size = 16;
        public ItemData[] validItems = Array.Empty<ItemData>();
    }

    [CreateAssetMenu(fileName = "New Persistent Inventory Definition", menuName = "World/Components/Persistent Inventory")]
    public sealed class PersistentInventoryDefinition : NodeComponentDefinition
    {
        public override Type DataType =>
            typeof(PersistentInventoryData);
        
        protected override void InstallComponent(
            GameObject host,
            DiContainer container,
            NodeComponentSpawnContext context,
            ComponentDefinitionData data)
        {
            var configuration = (PersistentInventoryData)data;
            PersistentInventory component = host.GetComponent<PersistentInventory>();
            if (component == null)
                component = container.InstantiateComponent<PersistentInventory>(host);

            if (configuration.validItems is { Length: > 0 })
                component.Configure(
                    configuration.size,
                    configuration.validItems);
            else
                component.Initialize(configuration.size);
        }
    }
}
