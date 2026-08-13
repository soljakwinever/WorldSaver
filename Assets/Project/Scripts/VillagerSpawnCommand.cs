using System;
using System.Collections.Generic;
using IngameDebugConsole;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.Persistence;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    /// <summary>Development console command for spawning a persistent villager.</summary>
    public sealed class VillagerSpawnCommand : IInitializable, IDisposable
    {
        private const string ArchetypeResourcePath =
            "EntityArchetypes/archetypes.villager.DoggyVillager";
        private const int SearchRadius = 3;

        private static readonly Vector2Int[] SpawnOffsets = BuildSpawnOffsets();

        private readonly Chunkloader _chunkloader;

        public VillagerSpawnCommand(Chunkloader chunkloader)
        {
            _chunkloader = chunkloader ??
                           throw new ArgumentNullException(nameof(chunkloader));
        }

        public void Initialize()
        {
            DebugLogConsole.AddCommand(
                "villager.spawn",
                "Spawns a persistent test villager beside the player.",
                SpawnVillager);
        }

        public void Dispose()
        {
            DebugLogConsole.RemoveCommand(SpawnVillager);
        }

        private void SpawnVillager()
        {
            EntityArchetype archetype =
                Resources.Load<EntityArchetype>(ArchetypeResourcePath);
            if (!TryValidateArchetype(archetype, out string reason))
            {
                Debug.LogWarning(
                    $"Cannot spawn a villager: {reason}");
                return;
            }

            if (_chunkloader.track == null)
            {
                Debug.LogWarning(
                    "Cannot spawn a villager because the chunk loader has no tracked player.");
                return;
            }

            NodeData nodeData = archetype.NodeData;
            Vector2Int playerCell = Vector2Int.FloorToInt(
                _chunkloader.track.position);
            bool foundLoadedChunk = false;
            bool runtimeSpawningReady = false;

            foreach (Vector2Int offset in SpawnOffsets)
            {
                Vector2Int cell = playerCell + offset;
                if (!_chunkloader.TryGetLoadedChunk(
                        new Vector3Int(cell.x, cell.y), out Chunk chunk))
                {
                    continue;
                }

                foundLoadedChunk = true;
                if (!chunk.CanSpawnRuntimeEntity(nodeData))
                    continue;

                runtimeSpawningReady = true;
                if (!chunk.TrySpawnRuntimeEntity(
                        nodeData, cell, out PersistentEntity entity))
                {
                    continue;
                }

                Debug.Log(
                    $"Spawned villager '{nodeData.name}' ({entity.Id}) at " +
                    $"{entity.transform.position}.", entity);
                return;
            }

            if (!foundLoadedChunk)
            {
                Debug.LogWarning(
                    "Cannot spawn a villager because no nearby chunks are loaded.");
            }
            else if (!runtimeSpawningReady)
            {
                Debug.LogWarning(
                    "Cannot spawn a villager because persistent chunk restoration " +
                    "is not complete or its runtime archetype is not registered.");
            }
            else
            {
                Debug.LogWarning(
                    $"Cannot spawn a villager because no valid cell exists within " +
                    $"{SearchRadius} cells of the player.");
            }
        }

        private static bool TryValidateArchetype(
            EntityArchetype archetype,
            out string reason)
        {
            if (archetype == null)
            {
                reason = $"the archetype resource '{ArchetypeResourcePath}' is missing.";
                return false;
            }

            NodeData nodeData = archetype.NodeData;
            if (nodeData == null)
            {
                reason = $"archetype '{archetype.name}' has no NodeData.";
                return false;
            }

            foreach (ComponentDefinitionData component
                     in nodeData.persistentComponents ??
                        Array.Empty<ComponentDefinitionData>())
            {
                if (component is VillagerComponentData &&
                    component.ComponentDefinition is VillagerDefinition)
                {
                    reason = string.Empty;
                    return true;
                }
            }

            reason = $"NodeData '{nodeData.name}' has no valid villager component.";
            return false;
        }

        private static Vector2Int[] BuildSpawnOffsets()
        {
            List<Vector2Int> offsets = new();
            for (int y = -SearchRadius; y <= SearchRadius; y++)
            for (int x = -SearchRadius; x <= SearchRadius; x++)
            {
                if (x != 0 || y != 0)
                    offsets.Add(new Vector2Int(x, y));
            }

            offsets.Sort((left, right) =>
            {
                int distance = left.sqrMagnitude.CompareTo(right.sqrMagnitude);
                if (distance != 0)
                    return distance;

                int y = left.y.CompareTo(right.y);
                return y != 0 ? y : left.x.CompareTo(right.x);
            });
            return offsets.ToArray();
        }
    }
}
