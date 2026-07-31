using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes.SaveData;

namespace Project.Scripts.Gameplay
{
    public static class TownCoreRegistry
    {
        private static readonly HashSet<TownCore> Towns = new();

        public static IReadOnlyCollection<TownCore> All => Towns;
        public static event Action<TownCore> TownAdded;
        public static event Action<TownCore> TownRemoved;

        internal static void Register(TownCore town)
        {
            if (town != null && Towns.Add(town))
                TownAdded?.Invoke(town);
        }

        internal static void Unregister(TownCore town)
        {
            if (town != null && Towns.Remove(town))
                TownRemoved?.Invoke(town);
        }

        public static bool TryGetAvailable(
            NodeId persistentId,
            out TownCore town)
        {
            foreach (TownCore candidate in Towns)
            {
                if (candidate != null &&
                    candidate.IsAvailable &&
                    candidate.PersistentEntity != null &&
                    candidate.PersistentEntity.Id.Equals(persistentId))
                {
                    town = candidate;
                    return true;
                }
            }

            town = null;
            return false;
        }

        public static bool TryGetAvailableAtSpawnPoint(
            UnityEngine.Vector3 spawnPoint,
            out TownCore town)
        {
            foreach (TownCore candidate in Towns)
            {
                if (candidate != null &&
                    candidate.IsAvailable &&
                    (candidate.SpawnPoint - spawnPoint).sqrMagnitude < 0.0001f)
                {
                    town = candidate;
                    return true;
                }
            }

            town = null;
            return false;
        }
    }
}
