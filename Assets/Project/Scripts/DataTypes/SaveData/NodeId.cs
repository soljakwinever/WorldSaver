using System;
using UnityEngine;

namespace Project.Scripts.DataTypes.SaveData
{
    public readonly struct NodeId : IEquatable<NodeId>
    {
        public readonly ulong value;
        
        public NodeId(ulong value)
        {
            this.value = value;
        }
        
        public bool Equals(NodeId other)
        {
            return value == other.value;
        }

        public override bool Equals(object obj)
        {
            return obj is NodeId other && Equals(other);
        }
        
        public override int GetHashCode()
        {
            return value.GetHashCode();
        }

        public override string ToString()
        {
            return value.ToString("X16");
        }
        
        public static NodeId Create(
            uint worldSeed,
            Vector2Int chunk,
            ushort generatorType,
            ushort slot)
        {
            unchecked
            {
                ulong hash = worldSeed;

                hash ^= (ulong)(uint)chunk.x * 0x9E3779B185EBCA87UL;
                hash ^= (ulong)(uint)chunk.y * 0xC2B2AE3D27D4EB4FUL;
                hash ^= (ulong)generatorType * 0x165667B19E3779F9UL;
                hash ^= slot * 0x85EBCA77C2B2AE63UL;

                hash ^= hash >> 30;
                hash *= 0xBF58476D1CE4E5B9UL;
                hash ^= hash >> 27;
                hash *= 0x94D049BB133111EBUL;
                hash ^= hash >> 31;

                return new NodeId(hash);
            }
        }

        public static NodeId CreateRuntimeId(WorldIdentityState state)
        {
            const ulong RuntimeEntityMask = 1UL << 63;

            ulong id = RuntimeEntityMask | state.nextRuntimeEntityId;
            state.nextRuntimeEntityId++;
            
            return new NodeId(id);
        }
    }
}