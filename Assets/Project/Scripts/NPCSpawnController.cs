using System;
using System.Collections.Generic;
using Project.Scripts.Bus;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    /// <summary>Creates and releases chunk-owned NPC populations.</summary>
    public sealed class NPCSpawnController : IInitializable, IDisposable, ITickable
    {
        private readonly MapSignalBus _mapSignals;
        private readonly NPCSpawnPool _pool;
        private readonly WorldGeneration _worldGeneration;
        private readonly WorldData _worldData;
        private readonly ITimeController _time;
        private readonly INPCSpawnEnvironmentProvider _environment;
        private readonly Dictionary<Vector2Int, ChunkPopulation> _populations = new();
        private readonly List<Vector2Int> _pendingPersistentChunks = new();

        public NPCSpawnController(
            MapSignalBus mapSignals,
            NPCSpawnPool pool,
            WorldGeneration worldGeneration,
            WorldData worldData,
            ITimeController time,
            INPCSpawnEnvironmentProvider environment)
        {
            _mapSignals = mapSignals;
            _pool = pool;
            _worldGeneration = worldGeneration;
            _worldData = worldData;
            _time = time;
            _environment = environment;
        }

        public void Initialize()
        {
            _mapSignals.ChunkLoaded += OnChunkLoaded;
            _mapSignals.ChunkUnloaded += OnChunkUnloaded;
        }

        public void Dispose()
        {
            _mapSignals.ChunkLoaded -= OnChunkLoaded;
            _mapSignals.ChunkUnloaded -= OnChunkUnloaded;
        }

        public void Tick()
        {
            // Persistence restoration is asynchronous. Retry only chunks whose
            // persistent root was not ready at the initial ChunkLoaded signal.
            for (int i = _pendingPersistentChunks.Count - 1; i >= 0; i--)
            {
                Vector2Int position = _pendingPersistentChunks[i];
                if (!_populations.TryGetValue(position, out ChunkPopulation population))
                {
                    _pendingPersistentChunks.RemoveAt(i);
                    continue;
                }

                if (TrySpawnPersistentRules(position, population.Chunk, population))
                    _pendingPersistentChunks.RemoveAt(i);
            }
        }

        private void OnChunkLoaded(Vector2Int position, IChunk loaded)
        {
            if (loaded is not Chunk chunk || _populations.ContainsKey(position))
                return;

            ChunkPopulation population = new(chunk);
            _populations.Add(position, population);
            foreach (EnemySpawnRule rule in EnumerateRules())
                SpawnTransientRule(position, rule, population);

            if (!TrySpawnPersistentRules(position, chunk, population))
                _pendingPersistentChunks.Add(position);
        }

        private void OnChunkUnloaded(Vector2Int position)
        {
            if (!_populations.Remove(position, out ChunkPopulation population))
                return;

            foreach (NPCSpawnInstance npc in population.TransientNPCs)
                if (npc != null)
                    _pool.Despawn(npc);
            _pendingPersistentChunks.Remove(position);
        }

        private bool TrySpawnPersistentRules(
            Vector2Int chunkPosition,
            Chunk chunk,
            ChunkPopulation population)
        {
            bool ready = true;
            foreach (EnemySpawnRule rule in EnumerateRules())
            {
                if (rule == null ||
                    rule.persistence != NPCPersistence.Persistent ||
                    population.PersistentRules.Contains(rule))
                    continue;
                if (rule.persistentNodeData == null)
                    continue;
                if (!chunk.CanSpawnRuntimeEntity(rule.persistentNodeData))
                {
                    ready = false;
                    continue;
                }

                // Restored runtime entities are already present by the time the
                // persistence root becomes ready. Do not seed the rule again.
                if (!chunk.ContainsRuntimeEntity(rule.persistentNodeData))
                {
                    foreach (Vector2 position in GetSpawnPositions(chunkPosition, rule))
                        chunk.TrySpawnRuntimeEntity(rule.persistentNodeData, position, out _);
                }
                population.PersistentRules.Add(rule);
            }
            return ready;
        }

        private void SpawnTransientRule(
            Vector2Int chunkPosition,
            EnemySpawnRule rule,
            ChunkPopulation population)
        {
            if (rule == null ||
                rule.persistence != NPCPersistence.Transient ||
                rule.enemyData == null && rule.npcPrefab == null)
                return;

            foreach (Vector2 position in GetSpawnPositions(chunkPosition, rule))
            {
                GameObject prefab = rule.enemyData != null
                    ? rule.enemyData.visual
                    : rule.npcPrefab;
                if (prefab != null)
                    population.TransientNPCs.Add(
                        _pool.Spawn(prefab, position, rule.enemyData));
            }
        }

        private IEnumerable<Vector2> GetSpawnPositions(
            Vector2Int chunkPosition,
            EnemySpawnRule rule)
        {
            if (!AllowsCurrentWorldState(rule))
                yield break;

            int seed = HashCode.Combine(
                unchecked((int)_worldGeneration.Seed),
                chunkPosition.x,
                chunkPosition.y,
                rule.GetEntityId());
            System.Random random = new(seed);
            int desired = random.Next(rule.minimumPerChunk, rule.maximumPerChunk + 1);
            int accepted = 0;
            List<Vector2> positions = new();
            int attempts = Mathf.Max(8, desired * 8);
            int startX = chunkPosition.x * ChunkBuildResult.ChunkSize;
            int startY = chunkPosition.y * ChunkBuildResult.ChunkSize;

            for (int attempt = 0; attempt < attempts && accepted < desired; attempt++)
            {
                if (random.NextDouble() > rule.spawnChance)
                    continue;
                Vector2 candidate = new(
                    startX + (float)random.NextDouble() * ChunkBuildResult.ChunkSize,
                    startY + (float)random.NextDouble() * ChunkBuildResult.ChunkSize);
                Vector2Int cell = Vector2Int.FloorToInt(candidate);
                TerrainSample sample = _worldGeneration.GetTerrainSample(cell.x, cell.y);
                // Keep spawn eligibility consistent with WorldPathFindingMap.
                // A* rejects an unwalkable starting cell, so spawning on water
                // or a cliff leaves the NPC with no route before it can move.
                if (!IsNavigableSpawn(sample) ||
                    !AllowsBiome(rule, sample.biome) ||
                    positions.Exists(other =>
                        (other - candidate).sqrMagnitude <
                        rule.minimumSpacing * rule.minimumSpacing))
                    continue;

                positions.Add(candidate);
                accepted++;
                yield return candidate;
            }
        }

        internal static bool IsNavigableSpawn(TerrainSample sample) =>
            !sample.isWater && !sample.isCliff;

        private bool AllowsCurrentWorldState(EnemySpawnRule rule)
        {
            return rule.AllowsSeason(_time.Season) &&
                   rule.AllowsHour(_time.Hour) &&
                   (string.IsNullOrWhiteSpace(rule.requiredEvent) ||
                    _environment.IsEventActive(rule.requiredEvent)) &&
                   AllowsWeather(rule);
        }

        private bool AllowsWeather(EnemySpawnRule rule)
        {
            if (rule.requiredWeather == null || rule.requiredWeather.Count == 0)
                return true;

            foreach (DataTypes.WeatherData weather in rule.requiredWeather)
                if (weather != null &&
                    _environment.IsWeatherActive(weather.WeatherId))
                    return true;

            return false;
        }

        private static bool AllowsBiome(EnemySpawnRule rule, BiomeData biome)
        {
            if (rule.restrictedBiomes != null &&
                Array.IndexOf(rule.restrictedBiomes, biome) >= 0)
                return false;
            return rule.allowedBiomes == null ||
                   rule.allowedBiomes.Length == 0 ||
                   Array.IndexOf(rule.allowedBiomes, biome) >= 0;
        }

        private IEnumerable<EnemySpawnRule> EnumerateRules()
        {
            HashSet<EnemySpawnRule> visited = new();
            if (_worldData.enemySpawnRules == null)
                yield break;
            foreach (EnemySpawnRule root in _worldData.enemySpawnRules)
                foreach (EnemySpawnRule rule in Traverse(root, visited))
                    yield return rule;
        }

        private static IEnumerable<EnemySpawnRule> Traverse(
            EnemySpawnRule rule,
            HashSet<EnemySpawnRule> visited)
        {
            if (rule == null || !visited.Add(rule))
                yield break;
            yield return rule;
            if (rule.rules == null)
                yield break;
            foreach (EnemySpawnRule child in rule.rules)
                foreach (EnemySpawnRule descendant in Traverse(child, visited))
                    yield return descendant;
        }

        private sealed class ChunkPopulation
        {
            public readonly Chunk Chunk;
            public readonly List<NPCSpawnInstance> TransientNPCs = new();
            public readonly HashSet<EnemySpawnRule> PersistentRules = new();

            public ChunkPopulation(Chunk chunk) => Chunk = chunk;
        }
    }
}
