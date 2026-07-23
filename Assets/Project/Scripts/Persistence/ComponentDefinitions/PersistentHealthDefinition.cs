using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [CreateAssetMenu(fileName = "Persistent Health", menuName = "World/Persistence/Persistent Health", order = 0)]
    public class PersistentHealthDefinition : NodeComponentDefinition
    {
        [SerializeField] private int maximumHealth = 100;

        public override void Install(GameObject host, DiContainer container, NodeComponentSpawnContext context)
        {
            var component = container.InstantiateComponent<PersistentHealth>(host);
            
            component.Initialize(maximumHealth);
        }
    }
}