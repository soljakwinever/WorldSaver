using Project.Scripts.DataTypes.SaveData;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    public struct NodeComponentSpawnContext
    {
        public readonly Object Node;
        public readonly EntityPersistenceKind PersistenceKind;

        public NodeComponentSpawnContext(Object node, EntityPersistenceKind persistenceKind)
        {
            Node = node;
            PersistenceKind = persistenceKind;
        }
    }
}