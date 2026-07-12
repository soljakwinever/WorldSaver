using System;
using System.Collections.Generic;

namespace Project.Scripts.DataTypes.SaveData
{
    public enum EntityPersistenceKind : byte
    {
        Procedural,
        Authored,
        RuntimeSpawned
    }

    public enum EntityExistenceState : byte
    {
        Exists,
        Removed
    }
    
    [Serializable]
    public sealed class PersistentEntityRecord
    {
        public EntityId id;
        
        //Needed for runtime spawned entities
        //Procedure entities can usually derive this from generation 
        public int archetypeId;
        
        public EntityPersistenceKind persistenceKind;
        public EntityExistenceState existenceState;

        public long lastSimulatedTick;

        public List<PersistenceComponentRecord> contents = new();
    }

    [Serializable]
    public sealed class PersistenceComponentRecord
    {
        public ushort typeId;
        public ushort version;
        public byte[] data;
    }
}