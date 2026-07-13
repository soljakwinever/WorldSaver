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
        public NodeId id;
        
        //Needed for runtime spawned entities
        //Procedure entities can usually derive this from generation 
        public int archetypeId;
        
        public EntityPersistenceKind persistenceKind;
        public EntityExistenceState existenceState;

        public long lastSimulatedTick;

        public List<PersistenceComponentRecord> components = new();

        public bool HasPersistentChanges => components.Count > 0;
        
        public bool MustBeSaved
        {
            get
            {
                if (persistenceKind == EntityPersistenceKind.RuntimeSpawned)
                {
                    return existenceState == EntityExistenceState.Exists;
                }

                return HasPersistentChanges;
            }
        }

        public void Compact()
        {
            components ??= new List<PersistenceComponentRecord>();
            
            /*
             * A removed procedural or authored entity only needs it's identity and tombstone state
             * It's component data can no longer affect a live entity and would merely become save file bloat
             *
             * A removed runtime spawned entity should normally be deleted from
             * the owning chunk state entirely rather than be retained as a tombstone
             */
            if (existenceState == EntityExistenceState.Removed)
            {
                components.Clear();
                lastSimulatedTick = 0;
                return;
            }
            
            /*
             * Keep the final occurence of each component type. This protects the
             * save data if a component was accidentally captured more than once twice
             */
            HashSet<ushort> seen = new();

            for (int i = components.Count - 1; i >= 0; i--)
            {
                PersistenceComponentRecord component = components[i];

                if (component == null ||
                    component.isAtBaseline ||
                    component.data == null ||
                    component.data.Length == 0)
                {
                    components.RemoveAt(i);
                    continue;
                }

                if (!seen.Add(component.typeId))
                {
                    components.RemoveAt(i);
                }
            }
            
            /*
             * Stable ordering makes binary saves deterministic and produces
             * cleaner comparisons, hashes, and debugging output
             */
            components.Sort(static (left,right) => left.typeId.CompareTo(right.typeId));
            
            /*
             * Generated and authored entities with no remaining deltas do not
             * need a simulation timestamp. Their entire record will normally be removed
             * by ChunkState.Compact()
             *
             * Runtime entities keep the timestamp because the entity record itself
             * must remain
             */
            
            if(persistenceKind != EntityPersistenceKind.RuntimeSpawned &&
                components.Count == 0)
            {
                lastSimulatedTick = 0;
            }
        }
        
        public static PersistentEntityRecord CreateTombstone(
            NodeId id, 
            EntityPersistenceKind persistenceKind)
        {
            if (persistenceKind == EntityPersistenceKind.RuntimeSpawned)
            {
                throw new ArgumentException("Runtime spawned entities should be removed from the save " +
                                            "state rather than represented by tombstones", nameof(persistenceKind));
            }
            return new PersistentEntityRecord
            {
                id = id,
                persistenceKind = persistenceKind,
                existenceState = EntityExistenceState.Removed
            };
        }

        public PersistentEntityRecord CreateSnapshot()
        {
            throw new NotImplementedException();
        }
    }

    [Serializable]
    public sealed class PersistenceComponentRecord
    {
        public ushort typeId;
        public ushort version;
        public byte[] data;
        
        public bool isAtBaseline;
    }
}