using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            EntityPersistenceKind persistenceKind,
            long currentTick
            )
        {
            PersistentEntityRecord record = new()
            {
                id = entity.Id,
                archetypeId = entity.ArchetypeId,
                persistenceKind = persistenceKind,
                existenceState = EntityExistenceState.Exists,
                lastSimulatedTick = currentTick
            };

            foreach (IPersistentComponent component in entity.GetPersistentComponents())
            {
                if(component.IsAtBaseline()) continue;

                using MemoryStream stream = new();
                using BinaryWriter writer = new(stream);
                
                component.WriteState(writer);
                
                record.components.Add(new PersistenceComponentRecord()
                {
                    typeId = component.PersistentTypeId,
                    version = component.PersistentVersion,
                    data = stream.ToArray()
                });
            }

            return record;
        }

        public static void Restore(
            PersistentEntity entity,
            PersistentEntityRecord record
        )
        {
            Dictionary<ushort, IPersistentComponent> components
                = entity.GetPersistentComponents()
                    .ToDictionary(x=> x.PersistentTypeId);

            foreach (var savedComponent in record.components)
            {
                if (!components.TryGetValue(
                        savedComponent.typeId,
                        out IPersistentComponent component))
                {
                    Debug.LogWarning($"Entity {entity.Id} has unknown persistent component {savedComponent.typeId}");
                    continue;
                }
                
                using MemoryStream stream = new(savedComponent.data);
                using BinaryReader reader = new(stream);
                
                component.ReadState(reader, savedComponent.version);
            }
            
        }
    }
}