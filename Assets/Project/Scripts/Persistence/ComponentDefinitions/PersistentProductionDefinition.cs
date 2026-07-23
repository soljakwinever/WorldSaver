using Project.Scripts.DataTypes;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [CreateAssetMenu(fileName = "Persistent Production Definition", menuName = "World/Persistence/Persistent Production", order = 0)]
    public class PersistentProductionDefinition : NodeComponentDefinition
    {
        public ItemData itemData;
        
        public override void Install(GameObject host, DiContainer container, NodeComponentSpawnContext context)
        {
            
        }
    }
}