using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [Serializable]
    public sealed class PlaneEntranceComponentData : ComponentDefinitionData
    {
        public string destinationPlaneId = "underground";
        public string interactionPrompt = "Descend underground";
        [Min(0)] public int destinationSearchRadius = 64;
        [Min(0)] public int destinationClearanceRadius = 2;
    }

    [CreateAssetMenu(
        fileName = "Plane Entrance Component Definition",
        menuName = "World/Components/Plane Entrance")]
    public sealed class PlaneEntranceComponentDefinition :
        NodeComponentDefinition
    {
        public override Type DataType =>
            typeof(PlaneEntranceComponentData);

        protected override void InstallComponent(
            GameObject host,
            DiContainer container,
            NodeComponentSpawnContext context,
            ComponentDefinitionData data)
        {
            var configuration = (PlaneEntranceComponentData)data;
            PlaneEntranceComponent component =
                host.GetComponent<PlaneEntranceComponent>();
            if (component == null)
            {
                component =
                    container.InstantiateComponent<PlaneEntranceComponent>(
                        host);
            }
            else
            {
                container.Inject(component);
            }

            component.Initialize(
                configuration.destinationPlaneId,
                configuration.interactionPrompt,
                configuration.destinationSearchRadius,
                configuration.destinationClearanceRadius);
        }
    }
}
