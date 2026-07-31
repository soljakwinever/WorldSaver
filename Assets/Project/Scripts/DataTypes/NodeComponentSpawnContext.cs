using Project.Scripts.DataTypes.SaveData;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    /// <summary>
    /// Runtime ownership attached to a player-placed persistent entity.
    /// Empty village/faction values mean the placer has no such affiliation.
    /// </summary>
    public readonly struct AccessIdentity
    {
        public readonly string OwnerId;
        public readonly string VillageId;
        public readonly string FactionId;

        public AccessIdentity(
            string ownerId,
            string villageId,
            string factionId)
        {
            OwnerId = Normalize(ownerId);
            VillageId = Normalize(villageId);
            FactionId = Normalize(factionId);
        }

        private static string Normalize(string value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    public struct NodeComponentSpawnContext
    {
        public readonly Object Node;
        public readonly Object Chunk;
        public readonly EntityPersistenceKind PersistenceKind;
        public readonly AccessIdentity AccessIdentity;

        public NodeComponentSpawnContext(
            Object node,
            Object chunk,
            EntityPersistenceKind persistenceKind,
            AccessIdentity accessIdentity = default)
        {
            Node = node;
            Chunk = chunk;
            PersistenceKind = persistenceKind;
            AccessIdentity = accessIdentity;
        }
    }
}
