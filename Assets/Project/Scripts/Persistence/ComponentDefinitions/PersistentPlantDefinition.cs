using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [Serializable]
    public sealed class PersistentPlantComponentData : ComponentDefinitionData
    {
        public PlantData plant;
    }

    [CreateAssetMenu(fileName = "Persistent Plant", menuName = "World/Persistence/Persistent Plant")]
    public sealed class PersistentPlantDefinition : NodeComponentDefinition
    {
        public override Type DataType => typeof(PersistentPlantComponentData);

        protected override void InstallComponent(GameObject host, DiContainer container,
            NodeComponentSpawnContext context, ComponentDefinitionData data)
        {
            PlantData plant = ((PersistentPlantComponentData)data).plant;
            if (plant == null) throw new InvalidOperationException("Plant data is required.");
            PersistentHealth health = host.GetComponent<PersistentHealth>() ??
                                      container.InstantiateComponent<PersistentHealth>(host);
            health.Initialize(plant.maximumHealth);
            PersistentPlant component = host.GetComponent<PersistentPlant>() ??
                                        container.InstantiateComponent<PersistentPlant>(host);
            component.Initialize(plant, context.Chunk as IPlantTileContext);
        }
    }
}
