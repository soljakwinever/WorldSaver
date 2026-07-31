using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [Serializable]
    public sealed class PersistentHealthData : ComponentDefinitionData
    {
        [Min(1)] public int maximumHealth = 100;
    }

    [CreateAssetMenu(fileName = "Persistent Health", menuName = "World/Persistence/Persistent Health", order = 0)]
    public class PersistentHealthDefinition : NodeComponentDefinition
    {
        public override Type DataType => typeof(PersistentHealthData);

        protected override void InstallComponent(
            GameObject host,
            DiContainer container,
            NodeComponentSpawnContext context,
            ComponentDefinitionData data)
        {
            var configuration = (PersistentHealthData)data;
            var component = host.GetComponent<PersistentHealth>();
            if (component == null)
            {
                component =
                    container.InstantiateComponent<PersistentHealth>(host);
            }
            
            component.Initialize(configuration.maximumHealth);
        }
    }
}
