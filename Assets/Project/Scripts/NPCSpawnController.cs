using System;
using System.Collections.Generic;
using System.Linq;
using Project.Scripts.AI;
using Project.Scripts.Bus;
using Project.Scripts.Core;
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
        private readonly IWorldClock _worldClock;
        private readonly ITimeController _time;
        private readonly INPCSpawnEnvironmentProvider _environment;
        private readonly IEventService _events;
        private readonly Dictionary<Vector2Int, ChunkPopulation> _populations = new();
        private readonly List<Vector2Int> _pendingPersistentChunks = new();
        private readonly Dictionary<EnemySpawnRule, long> _nextRuleSpawnTicks = new();
        private long _nextTransientRecycleTick;

        public NPCSpawnController(
            MapSignalBus mapSignals,
            NPCSpawnPool pool,
            WorldGeneration worldGeneration,
            WorldData worldData,
            IWorldClock worldClock,
            ITimeController time,
            INPCSpawnEnvironmentProvider environment,
            IEventService events)
        {
            _mapSignals = mapSignals;
            _pool = pool;
            _worldGeneration = worldGeneration;
            _worldData = worldData;
            _worldClock = worldClock;
            _time = time;
            _environment = environment;
            _events = events;
        }

        public void Initialize()
        {
            _mapSignals.ChunkLoaded += OnChunkLoaded;
            _mapSignals.ChunkUnloaded += OnChunkUnloaded;
            ScheduleNextTransientRecycleCheck();
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

                if (TryInitializePopulation(
                        position,
                        population.Chunk,
                        population))
                    _pendingPersistentChunks.RemoveAt(i);
            }

            long currentTick = _worldClock.CurrentTick;
            if (currentTick >= _nextTransientRecycleTick)
            {
                RecycleTransientNPCs(currentTick);
                ScheduleNextTransientRecycleCheck();
            }

            SpawnUnderfilledTransientRules(currentTick);
        }

        private void OnChunkLoaded(Vector2Int position, IChunk loaded)
        {
            if (loaded is not Chunk chunk || _populations.ContainsKey(position))
                return;

            ChunkPopulation population = new(chunk);
            _populations.Add(position, population);

            if (!TryInitializePopulation(position, chunk, population))
                _pendingPersistentChunks.Add(position);
        }

        private bool TryInitializePopulation(
            Vector2Int position,
            Chunk chunk,
            ChunkPopulation population)
        {
            if (!chunk.IsPersistenceRestoreCompleted)
                return false;

            if (!population.IsInitialized)
            {
                population.IsInitialized = true;
                foreach (EnemySpawnRule rule in EnumerateRules())
                    SpawnTransientRule(position, rule, population);
            }

            return TrySpawnPersistentRules(position, chunk, population);
        }

        private void OnChunkUnloaded(Vector2Int position)
        {
            if (!_populations.Remove(position, out ChunkPopulation population))
                return;

            foreach (TransientPopulationEntry entry in population.TransientNPCs)
                if (entry.Instance != null)
                    _pool.Despawn(entry.Instance);
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
                !HasTransientSpawnDefinition(rule))
                return;

            foreach (Vector2 position in GetSpawnPositions(chunkPosition, rule))
            {
                if (!IsUnderPopulationCap(
                        CountLivingTransient(rule),
                        rule.maxAllowed))
                    break;

                TrySpawnTransient(
                    rule,
                    position,
                    population,
                    _worldClock.CurrentTick,
                    population.TransientNPCs.Count);
            }
        }

        private void SpawnUnderfilledTransientRules(long currentTick)
        {
            Camera camera = Camera.main;
            if (camera == null || _populations.Count == 0)
                return;

            float viewportMargin =
                Mathf.Max(0f, _worldData.transientOffscreenViewportMargin);
            foreach (EnemySpawnRule rule in EnumerateRules())
            {
                if (rule == null ||
                    rule.persistence != NPCPersistence.Transient ||
                    !HasTransientSpawnDefinition(rule) ||
                    rule.maxAllowed <= 0)
                {
                    continue;
                }

                int interval = Mathf.Max(1, rule.spawnIntervalTicks);
                if (!_nextRuleSpawnTicks.TryGetValue(rule, out long nextTick))
                {
                    _nextRuleSpawnTicks[rule] = currentTick + interval;
                    continue;
                }
                if (currentTick < nextTick)
                    continue;

                _nextRuleSpawnTicks[rule] = currentTick + interval;
                if (!AllowsCurrentWorldState(rule) ||
                    !IsUnderPopulationCap(
                        CountLivingTransient(rule),
                        rule.maxAllowed) ||
                    !TryGetPeriodicSpawnPosition(
                        rule,
                        currentTick,
                        camera,
                        viewportMargin,
                        out ChunkPopulation population,
                        out Vector2 position))
                {
                    continue;
                }

                TrySpawnTransient(
                    rule,
                    position,
                    population,
                    currentTick,
                    unchecked((int)currentTick));
            }
        }

        private bool TryGetPeriodicSpawnPosition(
            EnemySpawnRule rule,
            long currentTick,
            Camera camera,
            float viewportMargin,
            out ChunkPopulation population,
            out Vector2 position)
        {
            var candidates =
                new List<KeyValuePair<Vector2Int, ChunkPopulation>>();
            foreach (KeyValuePair<Vector2Int, ChunkPopulation> candidate
                     in _populations)
            {
                if (candidate.Value.IsInitialized)
                    candidates.Add(candidate);
            }
            int startIndex = candidates.Count == 0
                ? 0
                : (int)((uint)HashCode.Combine(
                    rule.GetEntityId(),
                    unchecked((int)currentTick)) %
                    (uint)candidates.Count);

            for (int offset = 0; offset < candidates.Count; offset++)
            {
                KeyValuePair<Vector2Int, ChunkPopulation> candidate =
                    candidates[(startIndex + offset) % candidates.Count];
                if (TryGetOffscreenSpawnPosition(
                        candidate.Key,
                        rule,
                        currentTick,
                        offset,
                        camera,
                        viewportMargin,
                        out position))
                {
                    population = candidate.Value;
                    return true;
                }
            }

            population = null;
            position = default;
            return false;
        }

        private bool TrySpawnTransient(
            EnemySpawnRule rule,
            Vector2 position,
            ChunkPopulation population,
            long spawnTick,
            int salt)
        {
            NPCSpawnInstance instance =
                CreateTransientInstance(rule, position, spawnTick, salt);
            if (instance == null)
                return false;

            population.TransientNPCs.Add(
                new TransientPopulationEntry(instance, rule, spawnTick));
            return true;
        }

        private NPCSpawnInstance CreateTransientInstance(
            EnemySpawnRule rule,
            Vector2 position,
            long spawnTick,
            int salt)
        {
            int seed = HashCode.Combine(
                unchecked((int)_worldGeneration.Seed),
                rule.GetEntityId(),
                Mathf.FloorToInt(position.x * 100f),
                Mathf.FloorToInt(position.y * 100f),
                unchecked((int)spawnTick),
                salt);
            EnemyData selectedEnemy =
                rule.SelectEnemyData((float)new System.Random(seed).NextDouble());
            if (!TryResolveTransientVisual(
                    selectedEnemy,
                    out GameObject prefab))
            {
                return null;
            }

            NPCSpawnInstance instance =
                _pool.Spawn(prefab, position, selectedEnemy);
            AiNodeRunner runner =
                instance.Visual.GetComponent<AiNodeRunner>();
            if (runner != null)
            {
                runner.Blackboard.Set(AiKeys.SpawnRule, rule);
                runner.Blackboard.Set(AiKeys.TimeController, _time);
            }
            return instance;
        }

        private static bool HasTransientSpawnDefinition(
            EnemySpawnRule rule)
        {
            if (rule.enemyData?.visual != null)
                return true;
            if (rule.variations == null)
                return false;

            foreach (EnemySpawnVariation variation in rule.variations)
                if (variation?.enemyData?.visual != null)
                    return true;

            return false;
        }

        public static bool TryResolveTransientVisual(
            EnemyData enemyData,
            out GameObject visual)
        {
            visual = enemyData != null ? enemyData.visual : null;
            return visual != null;
        }

        private int CountLivingTransient(EnemySpawnRule rule)
        {
            int count = 0;
            foreach (ChunkPopulation population in _populations.Values)
            {
                foreach (TransientPopulationEntry entry in population.TransientNPCs)
                {
                    if (ReferenceEquals(entry.Rule, rule) &&
                        entry.Instance != null &&
                        entry.Instance.Visual != null)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        public static bool IsUnderPopulationCap(
            int livingCount,
            int maxAllowed)
        {
            return maxAllowed > 0 &&
                   Mathf.Max(0, livingCount) < maxAllowed;
        }

        private void RecycleTransientNPCs(long currentTick)
        {
            Camera camera = Camera.main;
            float viewportMargin =
                Mathf.Max(0f, _worldData.transientOffscreenViewportMargin);

            foreach (KeyValuePair<Vector2Int, ChunkPopulation> pair in _populations)
            {
                List<TransientPopulationEntry> entries =
                    pair.Value.TransientNPCs;
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    TransientPopulationEntry entry = entries[i];
                    NPCSpawnInstance instance = entry.Instance;

                    // Enemy death destroys the visual but leaves the pooled
                    // shell owned by this population.
                    if (instance == null || instance.Visual == null)
                    {
                        if (instance != null)
                            _pool.Despawn(instance);
                        entries.RemoveAt(i);
                        continue;
                    }

                    EnemySpawnRule rule = entry.Rule;
                    bool isOffscreen =
                        camera != null &&
                        IsOffscreen(
                            camera,
                            instance.Visual.transform.position,
                            viewportMargin);

                    // Invalid-hour enemies are population cleanup, not normal
                    // recycling. Remove them as soon as they are safely out of
                    // view, without waiting for age/idle thresholds and without
                    // creating a replacement while the rule is inactive.
                    if (ShouldPrioritizeTimeDespawn(
                            rule,
                            _time.Hour,
                            isOffscreen))
                    {
                        _pool.Despawn(instance);
                        entries.RemoveAt(i);
                        continue;
                    }

                    if (!IsMarkedIdle(instance))
                    {
                        entry.IdleSinceTick = null;
                        continue;
                    }

                    entry.IdleSinceTick ??= currentTick;
                    if (camera == null ||
                        rule == null ||
                        !rule.recycleOffscreenIdle ||
                        !isOffscreen ||
                        !ShouldRecycleTransient(
                            currentTick,
                            entry.SpawnTick,
                            entry.IdleSinceTick,
                            rule.minimumLifetimeTicks,
                            rule.idleTicksBeforeRecycle) ||
                        !TryGetOffscreenSpawnPosition(
                            pair.Key,
                            rule,
                            currentTick,
                            entry.Generation + 1,
                            camera,
                            viewportMargin,
                            out Vector2 replacementPosition))
                    {
                        continue;
                    }

                    NPCSpawnInstance replacement = CreateTransientInstance(
                        rule,
                        replacementPosition,
                        currentTick,
                        entry.Generation + 1);
                    if (replacement == null)
                        continue;

                    _pool.Despawn(instance);
                    entry.Instance = replacement;
                    entry.SpawnTick = currentTick;
                    entry.IdleSinceTick = null;
                    entry.Generation++;
                }
            }
        }

        public static bool ShouldPrioritizeTimeDespawn(
            EnemySpawnRule rule,
            int currentHour,
            bool isOffscreen)
        {
            return rule != null &&
                   isOffscreen &&
                   !rule.AllowsHour(currentHour);
        }

        private bool TryGetOffscreenSpawnPosition(
            Vector2Int chunkPosition,
            EnemySpawnRule rule,
            long currentTick,
            int generation,
            Camera camera,
            float viewportMargin,
            out Vector2 position)
        {
            int seed = HashCode.Combine(
                unchecked((int)_worldGeneration.Seed),
                chunkPosition.x,
                chunkPosition.y,
                rule.GetEntityId(),
                unchecked((int)currentTick),
                generation);
            System.Random random = new(seed);
            int startX = chunkPosition.x * ChunkBuildResult.ChunkSize;
            int startY = chunkPosition.y * ChunkBuildResult.ChunkSize;
            float minimumSpacingSquared =
                rule.minimumSpacing * rule.minimumSpacing;

            for (int attempt = 0; attempt < 64; attempt++)
            {
                Vector2 candidate = new(
                    startX + (float)random.NextDouble() * ChunkBuildResult.ChunkSize,
                    startY + (float)random.NextDouble() * ChunkBuildResult.ChunkSize);
                Vector2Int cell = Vector2Int.FloorToInt(candidate);
                TerrainSample sample =
                    _worldGeneration.GetTerrainSample(cell.x, cell.y);
                if (!IsNavigableSpawn(sample) ||
                    TileReservationSystem.IsReserved(cell) ||
                    !AllowsBiome(rule, sample.biome) ||
                    !IsOffscreen(camera, candidate, viewportMargin) ||
                    IsTooCloseToPopulation(
                        candidate,
                        minimumSpacingSquared))
                {
                    continue;
                }

                position = candidate;
                return true;
            }

            position = default;
            return false;
        }

        private bool IsTooCloseToPopulation(
            Vector2 candidate,
            float minimumSpacingSquared)
        {
            if (minimumSpacingSquared <= 0f)
                return false;

            foreach (ChunkPopulation population in _populations.Values)
            {
                foreach (TransientPopulationEntry entry in population.TransientNPCs)
                {
                    if (entry.Instance == null || entry.Instance.Visual == null)
                        continue;

                    Vector2 existing =
                        entry.Instance.Visual.transform.position;
                    if ((existing - candidate).sqrMagnitude <
                        minimumSpacingSquared)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsMarkedIdle(NPCSpawnInstance instance)
        {
            AiNodeRunner runner =
                instance.Visual.GetComponent<AiNodeRunner>();
            return runner != null &&
                   runner.Blackboard.GetOrDefault(AiKeys.IsIdle);
        }

        public static bool ShouldRecycleTransient(
            long currentTick,
            long spawnTick,
            long? idleSinceTick,
            int minimumLifetimeTicks,
            int minimumIdleTicks)
        {
            return idleSinceTick.HasValue &&
                   HasElapsed(
                       currentTick,
                       spawnTick,
                       Mathf.Max(0, minimumLifetimeTicks)) &&
                   HasElapsed(
                       currentTick,
                       idleSinceTick.Value,
                       Mathf.Max(1, minimumIdleTicks));
        }

        internal static bool IsOffscreen(
            Camera camera,
            Vector3 worldPosition,
            float viewportMargin)
        {
            if (camera == null)
                return false;

            float margin = Mathf.Max(0f, viewportMargin);
            Vector3 viewport = camera.WorldToViewportPoint(worldPosition);
            return viewport.z <= 0f ||
                   viewport.x < -margin ||
                   viewport.x > 1f + margin ||
                   viewport.y < -margin ||
                   viewport.y > 1f + margin;
        }

        private static bool HasElapsed(
            long currentTick,
            long startingTick,
            int duration)
        {
            return currentTick >= startingTick &&
                   currentTick - startingTick >= duration;
        }

        private void ScheduleNextTransientRecycleCheck()
        {
            _nextTransientRecycleTick =
                _worldClock.CurrentTick +
                Mathf.Max(1, _worldData.transientRecycleCheckIntervalTicks);
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

        public static bool IsNavigableSpawn(TerrainSample sample) =>
            sample.IsWalkable;

        private bool AllowsCurrentWorldState(EnemySpawnRule rule)
        {
            return rule.AllowsSeason(_time.Season) &&
                   rule.AllowsHour(_time.Hour) &&
                   (string.IsNullOrWhiteSpace(rule.requiredEvent) ||
                    _environment.IsEventActive(rule.requiredEvent)) &&
                   rule.AllowsTemperature(
                       _environment.GetAmbientTemperature()) &&
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
            IReadOnlyCollection<EnemySpawnRule> disabled =
                _worldGeneration.AllowEventNPCSpawnRules
                    ? _events?.DisabledEnemySpawnRules
                    : null;
            foreach (EnemySpawnRule root in _worldGeneration.EnemySpawnRules)
                foreach (EnemySpawnRule rule in Traverse(root, visited, disabled))
                    yield return rule;
            if (!_worldGeneration.AllowEventNPCSpawnRules ||
                _events?.ActiveEnemySpawnRules == null)
            {
                yield break;
            }
            foreach (EnemySpawnRule root in _events.ActiveEnemySpawnRules)
                foreach (EnemySpawnRule rule in Traverse(root, visited, disabled))
                    yield return rule;
        }

        private static IEnumerable<EnemySpawnRule> Traverse(
            EnemySpawnRule rule,
            HashSet<EnemySpawnRule> visited,
            IReadOnlyCollection<EnemySpawnRule> disabled)
        {
            if (rule == null ||
                disabled?.Contains(rule) == true ||
                !visited.Add(rule))
                yield break;
            yield return rule;
            if (rule.rules == null)
                yield break;
            foreach (EnemySpawnRule child in rule.rules)
                foreach (EnemySpawnRule descendant in
                         Traverse(child, visited, disabled))
                    yield return descendant;
        }

        private sealed class ChunkPopulation
        {
            public readonly Chunk Chunk;
            public readonly List<TransientPopulationEntry> TransientNPCs = new();
            public readonly HashSet<EnemySpawnRule> PersistentRules = new();
            public bool IsInitialized;

            public ChunkPopulation(Chunk chunk) => Chunk = chunk;
        }

        private sealed class TransientPopulationEntry
        {
            public NPCSpawnInstance Instance;
            public readonly EnemySpawnRule Rule;
            public long SpawnTick;
            public long? IdleSinceTick;
            public int Generation;

            public TransientPopulationEntry(
                NPCSpawnInstance instance,
                EnemySpawnRule rule,
                long spawnTick)
            {
                Instance = instance;
                Rule = rule;
                SpawnTick = spawnTick;
            }
        }
    }
}
