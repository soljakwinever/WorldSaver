using Project.Scripts.DataTypes.SaveData;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    public struct NodeComponentSpawnContext
    {
        public readonly Object Node;
        public readonly Object Chunk;
        public readonly EntityPersistenceKind PersistenceKind;

        public NodeComponentSpawnContext(
            Object node,
            Object chunk,
            EntityPersistenceKind persistenceKind)
        {
            Node = node;
            Chunk = chunk;
            PersistenceKind = persistenceKind;
        }
    }
}
