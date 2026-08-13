using System;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Gameplay;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [Serializable]
    public sealed class TownCoreData : ComponentDefinitionData
    {
        public string initialName = "New Village";
        public string windowTitle = "Town Core";
        public string interactionPrompt = "Visit town shrine";
        [Min(0f)] public float spawnPointOffset = 1.25f;
        [Min(1)] public int maximumHealth = 250;
        [Min(1)] public int offeringInventorySize = 16;
        [Min(1)] public int stockpileInventorySize = 32;
        [Tooltip("Food granted for each resident joining a procedurally generated town.")]
        public ItemData starterFoodItem;
        [Min(0)] public int starterFoodPerResident = 2;
        [Min(0f)] public float townRadius = 12f;
        [Min(0f)] public float resourceRadius = 30f;
        [Min(0)] public int maximumPopulation = 10;
        [Min(0f)] public float maximumMana = 100f;
        [Min(0f)] public float passiveManaPerTick = 0.01f;
        [Min(1)] public long ticksPerOffering = 10;
        [Min(1)] public long buildingRefreshTicks = 10;
        public LayerMask buildingLayerMask = ~0;
        public TownEffect[] effects = Array.Empty<TownEffect>();
        public TownUpgradeDefinition[] upgrades =
            Array.Empty<TownUpgradeDefinition>();
    }

    [CreateAssetMenu(
        fileName = "Town Core",
        menuName = "World/Components/Town Core")]
    public sealed class TownCoreDefinition : NodeComponentDefinition
    {
        public override Type DataType => typeof(TownCoreData);

        protected override void InstallComponent(
            GameObject host,
            DiContainer container,
            NodeComponentSpawnContext context,
            ComponentDefinitionData data)
        {
            var configuration = (TownCoreData)data;
            PersistentHealth health = host.GetComponent<PersistentHealth>();
            if (health == null)
                health = container.InstantiateComponent<PersistentHealth>(host);
            health.Initialize(Math.Max(1, configuration.maximumHealth));

            PersistentInventory inventory =
                host.GetComponent<PersistentInventory>();
            if (inventory == null)
                inventory =
                    container.InstantiateComponent<PersistentInventory>(host);
            inventory.Initialize(Math.Max(
                1,
                configuration.offeringInventorySize));

            TownStockpile stockpile = host.GetComponent<TownStockpile>();
            if (stockpile == null)
                stockpile = container.InstantiateComponent<TownStockpile>(host);
            stockpile.Initialize(Math.Max(1, configuration.stockpileInventorySize));

            if (host.GetComponent<TownJobBoard>() == null)
                container.InstantiateComponent<TownJobBoard>(host);
            if (host.GetComponent<TownConstructionQueue>() == null)
                container.InstantiateComponent<TownConstructionQueue>(host);

            TownCore component = host.GetComponent<TownCore>();
            if (component == null)
                component = container.InstantiateComponent<TownCore>(host);

            // Discovery components resolve TownCore during Awake, so install
            // them only after the injected TownCore exists on the host.
            if (host.GetComponent<TownWorkDiscovery>() == null)
                container.InstantiateComponent<TownWorkDiscovery>(host);
            if (host.GetComponent<VillagerResourceDiscovery>() == null)
                container.InstantiateComponent<VillagerResourceDiscovery>(host);

            component.Initialize(
                configuration.initialName,
                configuration.townRadius,
                configuration.resourceRadius,
                configuration.maximumPopulation,
                configuration.maximumMana,
                configuration.passiveManaPerTick,
                configuration.ticksPerOffering,
                configuration.buildingRefreshTicks,
                configuration.buildingLayerMask,
                configuration.effects,
                configuration.upgrades,
                configuration.windowTitle,
                configuration.interactionPrompt,
                configuration.spawnPointOffset,
                context.PersistenceKind == EntityPersistenceKind.Procedural,
                configuration.starterFoodItem,
                configuration.starterFoodPerResident);
        }
    }
}
