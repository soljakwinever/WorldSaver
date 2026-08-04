using System;
using Project.Scripts.DataTypes;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [Serializable]
    public sealed class SpaceReservationData : ComponentDefinitionData
    {
        [Min(1)] public int width = 1;
        [Min(1)] public int height = 1;
        public int xOffset;
        public int yOffset;
    }

    [CreateAssetMenu(
        fileName = "Space Reservation",
        menuName = "World/Components/Space Reservation")]
    public sealed class SpaceReservationDefinition : NodeComponentDefinition
    {
        public override Type DataType => typeof(SpaceReservationData);

        protected override void InstallComponent(
            GameObject host,
            DiContainer container,
            NodeComponentSpawnContext context,
            ComponentDefinitionData data)
        {
            var configuration = (SpaceReservationData)data;
            SpaceReservationComponent component =
                host.GetComponent<SpaceReservationComponent>();
            if (component == null)
            {
                component =
                    container.InstantiateComponent<SpaceReservationComponent>(
                        host);
            }

            Node node = context.Node as Node;
            component.Initialize(
                node != null ? node.transform : host.transform,
                configuration.width,
                configuration.height);
        }
    }
}
