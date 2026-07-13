using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.DataTypes.SaveData
{
    [Serializable]
    public class ChunkState
    {
        public ushort localChunkIndex;
        public long lastSimulatedTick;
        
        public List<PersistentEntityRecord> entities = new();
        public List<PersistenceComponentRecord> components = new();
        
        public bool HasChanges => entities.Count > 0;

        public void Compact()
        {
            for (int i = entities.Count - 1; i >= 0; i--)
            {
                var entity = entities[i];

                entity.Compact();
                
                if(!entity.MustBeSaved)
                    entities.RemoveAt(i);
            }

            components.Clear();
            // for (int i = components.Count - 1; i >= 0; i--)
            // {
            //     if(components[i].IsAtBaseline)
            // }
        }

        public ChunkState CreateSnapshot()
        {
            ChunkState snapshot = new()
            {
                localChunkIndex = localChunkIndex,
                lastSimulatedTick = lastSimulatedTick
            };

            foreach (var entity in entities)
            {
                if(entity != null)
                    snapshot.entities.Add(entity.CreateSnapshot());
            }

            foreach (var component in components)
            {
                if(component != null)
                    snapshot.components.Add(component.CreateSnapshot());
            }
        }
    }

    [Serializable]
    public sealed class ChunkComponentRecord
    {
        public ushort version;
        public byte[] data;
    }
}