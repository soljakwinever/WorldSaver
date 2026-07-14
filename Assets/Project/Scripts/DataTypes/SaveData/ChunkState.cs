using System;
using System.Collections.Generic;

namespace Project.Scripts.DataTypes.SaveData
{
    [Serializable]
    public sealed class ChunkState
    {
        public ushort localChunkIndex;
        public long lastSimulatedTick;
        public List<PersistentEntityRecord> entities = new();
        public List<PersistenceComponentRecord> components = new();

        public bool HasChanges =>
            (entities != null && entities.Count > 0) ||
            (components != null && components.Count > 0);

        public void Compact()
        {
            entities ??= new List<PersistentEntityRecord>();
            components ??= new List<PersistenceComponentRecord>();

            for (int i = entities.Count - 1; i >= 0; i--)
            {
                PersistentEntityRecord entity = entities[i];

                if (entity == null)
                {
                    entities.RemoveAt(i);
                    continue;
                }

                entity.Compact();

                if (!entity.MustBeSaved)
                    entities.RemoveAt(i);
            }

            for (int i = components.Count - 1; i >= 0; i--)
            {
                PersistenceComponentRecord component = components[i];

                if (component == null ||
                    component.isAtBaseline ||
                    component.data == null ||
                    component.data.Length == 0)
                {
                    components.RemoveAt(i);
                }
            }

            entities.Sort(static (left, right) =>
                left.id.value.CompareTo(right.id.value));
            components.Sort(static (left, right) =>
                left.typeId.CompareTo(right.typeId));
        }

        public ChunkState CreateSnapshot()
        {
            ChunkState snapshot = new()
            {
                localChunkIndex = localChunkIndex,
                lastSimulatedTick = lastSimulatedTick
            };

            if (entities != null)
            {
                foreach (PersistentEntityRecord entity in entities)
                {
                    if (entity != null)
                        snapshot.entities.Add(entity.CreateSnapshot());
                }
            }

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
    }
}
