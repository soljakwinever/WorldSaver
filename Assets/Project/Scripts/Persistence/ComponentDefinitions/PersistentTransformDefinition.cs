using System;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Gameplay;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [Serializable]
    public sealed class PersistentTransformData : ComponentDefinitionData
    {
        public bool alwaysPersist = true;
    }

    [CreateAssetMenu(fileName = "Persistent Transform", menuName = "World/Persistence/Persistent Transform", order = 0)]
    public class PersistentTransformDefinition : NodeComponentDefinition
    {
        public override Type DataType =>
            typeof(PersistentTransformData);

        protected override void InstallComponent(
            GameObject host,
            DiContainer container,
            NodeComponentSpawnContext context,
            ComponentDefinitionData data)
        {
            var configuration = (PersistentTransformData)data;
            var component = container.InstantiateComponent<PersistentTransform>(host);

            bool runtimeSpawned = context.PersistenceKind == EntityPersistenceKind.RuntimeSpawned;
            
            var node = context.Node as Node;
            component.Initialize(
                configuration.alwaysPersist || runtimeSpawned,
                node.transform);
        }
    }
}
