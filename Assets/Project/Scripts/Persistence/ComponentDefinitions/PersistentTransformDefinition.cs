using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Gameplay;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [CreateAssetMenu(fileName = "Persistent Transform", menuName = "World/Persistence/Persistent Transform", order = 0)]
    public class PersistentTransformDefinition : NodeComponentDefinition
    {
        [SerializeField]
        private bool alwaysPersist = true;

        public override void Install(GameObject host, DiContainer container, NodeComponentSpawnContext context)
        {
            var component = container.InstantiateComponent<PersistentTransform>(host);

            bool runtimeSpawned = context.PersistenceKind == EntityPersistenceKind.RuntimeSpawned;
            
            var node = context.Node as Node;
            component.Initialize(alwaysPersist || runtimeSpawned, node.transform);
        }
    }
}