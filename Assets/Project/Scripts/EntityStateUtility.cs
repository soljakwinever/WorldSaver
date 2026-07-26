using System.Collections.Generic;
using System.IO;
using Project.Scripts.Core;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts
{
    public static class EntityStateUtility
    {
        public static PersistentEntityRecord Capture(
            PersistentEntity entity,
            long currentTick)
        {
            if (entity == null)
                throw new System.ArgumentNullException(nameof(entity));

            PersistentEntityRecord record = new()
            {
                id = entity.Id,
                archetypeId = entity.ArchetypeId,
                persistenceKind = entity.PersistenceKind,
                existenceState = EntityExistenceState.Exists,
                lastSimulatedTick = currentTick
            };

            HashSet<ushort> capturedTypes = new();
            foreach (IPersistentComponent component in entity.GetPersistentComponents())
            {
                if (!capturedTypes.Add(component.PersistentTypeId))
                {
                    Debug.LogError(
                        $"Entity {entity.Id} has duplicate persistent component " +
                        $"type {component.PersistentTypeId}.",
                        entity);
                    continue;
                }

                if (component.IsAtBaseline())
                    continue;

                using MemoryStream stream = new();
                using BinaryWriter writer = new(stream);

                component.WriteState(writer);

                record.components.Add(new PersistenceComponentRecord
                {
                    typeId = component.PersistentTypeId,
                    version = component.PersistentVersion,
                    data = stream.ToArray()
                });
            }

            record.Compact();
            return record;
        }

        public static void Restore(
            PersistentEntity entity,
            PersistentEntityRecord record)
        {
            if (entity == null)
                throw new System.ArgumentNullException(nameof(entity));
            if (record == null)
                throw new System.ArgumentNullException(nameof(record));

            Dictionary<ushort, IPersistentComponent> components = new();

            foreach (IPersistentComponent component in entity.GetPersistentComponents())
            {
                if (!components.TryAdd(component.PersistentTypeId, component))
                {
                    Debug.LogError(
                        $"Entity {entity.Id} has duplicate persistent component " +
                        $"type {component.PersistentTypeId}.",
                        entity);
                }
            }

            foreach (PersistenceComponentRecord saved in record.components)
            {
                if (!components.TryGetValue(saved.typeId, out IPersistentComponent component))
                {
                    Debug.LogWarning(
                        $"Entity {entity.Id} has no persistent component " +
                        $"for saved type {saved.typeId}.",
                        entity);
                    continue;
                }

                using MemoryStream stream = new(saved.data, writable: false);
                using BinaryReader reader = new(stream);
                component.ReadState(reader, saved.version);
            }
        }
    }
}
