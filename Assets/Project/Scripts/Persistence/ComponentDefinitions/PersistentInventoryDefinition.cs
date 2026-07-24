using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [CreateAssetMenu(fileName = "New Persistent Inventory Definition", menuName = "World/Components/Persistent Inventory")]
    public sealed class PersistentInventoryDefinition : NodeComponentDefinition
    {
        [SerializeField, Min(1)] private int size = 16;
        [SerializeField] private ItemData[] validItems = Array.Empty<ItemData>();
        
        public override void Install(GameObject host, DiContainer container, NodeComponentSpawnContext context)
        {
            PersistentInventory component = host.GetComponent<PersistentInventory>();
            if (component == null)
                component = container.InstantiateComponent<PersistentInventory>(host);

            if (validItems is { Length: > 0 })
                component.Configure(size, validItems);
            else
                component.Initialize(size);
        }
    }
}
