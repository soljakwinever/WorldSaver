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
        public int archetypeId;
        public EntityPersistenceKind persistenceKind;
        public EntityExistenceState existenceState;
        public long lastSimulatedTick;
        public List<PersistenceComponentRecord> components = new();

        public bool HasPersistentChanges => components != null && components.Count > 0;

        public bool MustBeSaved
        {
            get
            {
                if (existenceState == EntityExistenceState.Removed)
                    return persistenceKind != EntityPersistenceKind.RuntimeSpawned;

                return persistenceKind == EntityPersistenceKind.RuntimeSpawned ||
                       HasPersistentChanges;
            }
        }

        public void Compact()
        {
            components ??= new List<PersistenceComponentRecord>();

            if (existenceState == EntityExistenceState.Removed)
            {
                components.Clear();
                archetypeId = 0;
                lastSimulatedTick = 0;
                return;
            }

            HashSet<ushort> seen = new();

            for (int i = components.Count - 1; i >= 0; i--)
            {
                PersistenceComponentRecord component = components[i];

                if (component == null ||
                    component.isAtBaseline ||
                    component.data == null ||
                    component.data.Length == 0 ||
                    !seen.Add(component.typeId))
                {
                    components.RemoveAt(i);
                }
            }

            components.Sort(static (left, right) =>
                left.typeId.CompareTo(right.typeId));

            if (persistenceKind != EntityPersistenceKind.RuntimeSpawned &&
                components.Count == 0)
            {
                lastSimulatedTick = 0;
            }
        }

        public PersistentEntityRecord CreateSnapshot()
        {
            PersistentEntityRecord snapshot = new()
            {
                id = id,
                archetypeId = archetypeId,
                persistenceKind = persistenceKind,
                existenceState = existenceState,
                lastSimulatedTick = lastSimulatedTick
            };

            if (components != null)
            {
                foreach (PersistenceComponentRecord component in components)
                {
                    if (component != null)
                        snapshot.components.Add(component.CreateSnapshot());
                }
            }

            return snapshot;
        }

        public static PersistentEntityRecord CreateTombstone(
            NodeId id,
            EntityPersistenceKind persistenceKind)
        {
            if (persistenceKind == EntityPersistenceKind.RuntimeSpawned)
            {
                throw new ArgumentException(
                    "Runtime-spawned entities are deleted by removing their full record.",
                    nameof(persistenceKind));
            }

            return new PersistentEntityRecord
            {
                id = id,
                persistenceKind = persistenceKind,
                existenceState = EntityExistenceState.Removed
            };
        }
    }

    [Serializable]
    public sealed class PersistenceComponentRecord
    {
        public ushort typeId;
        public ushort version;
        public byte[] data = Array.Empty<byte>();
        public bool isAtBaseline;

        public PersistenceComponentRecord CreateSnapshot()
        {
            return new PersistenceComponentRecord
            {
                typeId = typeId,
                version = version,
                data = data == null ? Array.Empty<byte>() : (byte[])data.Clone(),
                isAtBaseline = isAtBaseline
            };
        }
    }
}
