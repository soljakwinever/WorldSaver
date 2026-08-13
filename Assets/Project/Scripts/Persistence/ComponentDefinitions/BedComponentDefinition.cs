using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [Serializable]
    public sealed class BedComponentData : ComponentDefinitionData
    {
        [Min(0f)] public float healthPerSecond = 2f;
        [Min(0f)] public float energyPerSecond = 8f;
        public string interactionPrompt = "Sleep until morning";
    }

    [CreateAssetMenu(fileName = "Bed", menuName = "World/Components/Bed")]
    public sealed class BedComponentDefinition : NodeComponentDefinition
    {
        public override Type DataType => typeof(BedComponentData);

        protected override void InstallComponent(GameObject host,
            DiContainer container, NodeComponentSpawnContext context,
            ComponentDefinitionData data)
        {
            BedComponentData configuration = (BedComponentData)data;
            BedComponent bed = host.GetComponent<BedComponent>();
            if (bed == null) bed = container.InstantiateComponent<BedComponent>(host);
            else container.Inject(bed);
            bed.Initialize(configuration.healthPerSecond,
                configuration.energyPerSecond, configuration.interactionPrompt,
                context.AccessIdentity.OwnerId);
        }
    }
}
