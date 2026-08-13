using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Entities;
using Project.Scripts.Gameplay;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [Serializable]
    public sealed class VillagerComponentData : ComponentDefinitionData
    {
        public string villagerName = "Villager";
        [Min(1)] public int maximumHealth = 100;
        [Min(0f)] public float movementSpeed = 3f;
        [Min(0f)] public float workRate = 1f;
        [Min(0f)] public float hungerDrainPerSecond = 0.2f;
        [Min(1)] public int inventorySize = 6;
        public VillagerRole role = VillagerRole.Generalist;
        public VillagerJobMask allowedJobs = VillagerJobMask.All;
    }

    [CreateAssetMenu(fileName = "Villager", menuName = "World/Components/Villager")]
    public sealed class VillagerDefinition : NodeComponentDefinition
    {
        public override Type DataType => typeof(VillagerComponentData);
        protected override void InstallComponent(GameObject host, DiContainer container,
            NodeComponentSpawnContext context, ComponentDefinitionData data)
        {
            VillagerComponentData configuration = (VillagerComponentData)data;
            VillagerEntityBridge bridge = host.GetComponent<VillagerEntityBridge>();
            if (bridge == null) bridge = container.InstantiateComponent<VillagerEntityBridge>(host);
            bridge.Initialize(configuration.maximumHealth, configuration.movementSpeed,
                configuration.workRate, configuration.hungerDrainPerSecond,
                configuration.inventorySize, configuration.role,
                configuration.allowedJobs, configuration.villagerName);
        }
    }
}
