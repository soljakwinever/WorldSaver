using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IngameDebugConsole;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using Project.Scripts.Persistence;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    public readonly struct FeatureBuildingSpawnResult
    {
        public readonly bool Success;
        public readonly string Reason;

        private FeatureBuildingSpawnResult(bool success, string reason)
        {
            Success = success;
            Reason = reason ?? string.Empty;
        }

        public static FeatureBuildingSpawnResult Succeeded() =>
            new(true, string.Empty);

        public static FeatureBuildingSpawnResult Failed(string reason) =>
            new(false, reason);
    }

    public sealed class FeatureBuildingService : IInitializable, IDisposable
    {
        private readonly WorldGeneration _worldGeneration;
        private readonly Chunkloader _chunkloader;
        private readonly IRegionRepository _regions;
        private readonly IWorldClock _clock;
        private readonly IClimateCoreInfluenceRegistry _climateCores;

        public FeatureBuildingService(
            WorldGeneration worldGeneration,
            Chunkloader chunkloader,
            IRegionRepository regions,
            IWorldClock clock,
            IClimateCoreInfluenceRegistry climateCores)
        {
            _worldGeneration = worldGeneration;
            _chunkloader = chunkloader;
            _regions = regions;
            _clock = clock;
            _climateCores = climateCores;
        }

        public void Initialize()
        {
            DebugLogConsole.AddCommand<string, int, int>(
                "featurebuilding.spawn",
                "Spawns a feature building by persistent ID in a region.",
                DebugSpawn,
                "buildingId",
                "regionX",
                "regionY");
            DebugLogConsole.AddCommand<string, int, int>(
                "feature.locate",
                "Locates a generated or runtime feature in a region.",
                DebugLocate,
                "featureId",
                "regionX",
                "regionY");
        }

        public void Dispose()
        {
            DebugLogConsole.RemoveCommand<string, int, int>(DebugSpawn);
            DebugLogConsole.RemoveCommand<string, int, int>(DebugLocate);
        }

        private async void DebugLocate(
            string featureId,
            int regionX,
            int regionY)
        {
            try
            {
                Vector2Int region = new(regionX, regionY);
                if (_worldGeneration.TryLocateFeatureInRegion(
                        featureId,
                        region,
                        out Vector2 generatedPosition))
                {
                    LogLocatedFeature(
                        featureId,
                        region,
                        generatedPosition,
                        "generated");
                    return;
                }

                FeatureBuildingData building = FindBuilding(featureId);
                (bool found, Vector2 runtimePosition) runtime =
                    building == null
                        ? (false, default)
                        : await TryLocateRuntimeBuildingAsync(
                            building,
                            region);
                if (runtime.found)
                {
                    LogLocatedFeature(
                        building.persistentId,
                        region,
                        runtime.runtimePosition,
                        "runtime");
                    return;
                }

                Debug.LogWarning(
                    $"Feature '{featureId}' was not found in region {region}.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private async void DebugSpawn(
            string buildingId,
            int regionX,
            int regionY)
        {
            try
            {
                FeatureBuildingSpawnResult result =
                    await TrySpawnWithResultAsync(
                    buildingId,
                    new Vector2Int(regionX, regionY));
                if (!result.Success)
                {
                    Debug.LogWarning(
                        $"Could not spawn feature building '{buildingId}' in " +
                        $"region ({regionX}, {regionY}): {result.Reason}");
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        public async Awaitable<bool> TrySpawnAsync(
            string buildingId,
            Vector2Int regionPosition)
        {
            FeatureBuildingSpawnResult result =
                await TrySpawnWithResultAsync(buildingId, regionPosition);
            return result.Success;
        }

        public async Awaitable<FeatureBuildingSpawnResult>
            TrySpawnWithResultAsync(
            string buildingId,
            Vector2Int regionPosition)
        {
            FeatureBuildingData building = FindBuilding(buildingId);
            if (building == null)
            {
                string available = string.Join(
                    ", ",
                    _worldGeneration.AllFeatureBuildings
                        .Select(candidate => candidate.persistentId)
                        .Where(id => !string.IsNullOrWhiteSpace(id)));
                return FeatureBuildingSpawnResult.Failed(
                    string.IsNullOrEmpty(available)
                        ? "no feature buildings are configured in the active world-generation preset."
                        : $"no feature building with that ID exists. Available IDs: {available}.");
            }
            if (building.entityArchetype == null)
            {
                return FeatureBuildingSpawnResult.Failed(
                    $"'{building.persistentId}' has no entity archetype assigned.");
            }
            if (building.entityArchetype.NodeData == null)
            {
                return FeatureBuildingSpawnResult.Failed(
                    $"archetype '{building.entityArchetype.name}' has no NodeData assigned.");
            }

            RuntimeRegion region = await _regions.GetReadyAsync(
                regionPosition,
                _clock.CurrentTick);
            if (_worldGeneration.TryLocateAnyFeatureInRegion(
                    regionPosition,
                    out FeatureData existingFeature,
                    out Vector2 existingFeaturePosition))
            {
                Vector2Int existingChunk =
                    WorldPartition.WorldToChunk(existingFeaturePosition);
                return FeatureBuildingSpawnResult.Failed(
                    $"generated feature '{existingFeature.persistentId}' is " +
                    $"already present in chunk {existingChunk} at " +
                    $"{existingFeaturePosition}.");
            }
            if (TryFindRuntimeFeatureBuilding(
                    region,
                    out FeatureBuildingData runtimeBuilding,
                    out Vector2 runtimePosition))
            {
                return FeatureBuildingSpawnResult.Failed(
                    $"runtime feature building '{runtimeBuilding.persistentId}' " +
                    $"is already present in chunk " +
                    $"{WorldPartition.WorldToChunk(runtimePosition)} at " +
                    $"{runtimePosition}.");
            }

            Vector2 position = GetRuntimeSpawnPosition(
                regionPosition,
                _worldGeneration.Seed,
                building.persistentId,
                building.placementOffset,
                _clock.CurrentTick);
            Vector2Int worldCell = Vector2Int.FloorToInt(position);

            if (_chunkloader.TryGetLoadedChunk(
                    new Vector3Int(worldCell.x, worldCell.y),
                    out Chunk loaded))
            {
                if (!loaded.IsPersistenceRestoreCompleted)
                {
                    return FeatureBuildingSpawnResult.Failed(
                        $"target chunk {loaded.Position} is still restoring its persistent state.");
                }
                if (!loaded.CanSpawnRuntimeEntity(
                        building.entityArchetype.NodeData))
                {
                    return FeatureBuildingSpawnResult.Failed(
                        $"archetype ID {building.entityArchetype.Id} is not registered for runtime spawning.");
                }
                if (!SpaceReservationUtility.CanPlace(
                        building.entityArchetype.NodeData,
                        position))
                {
                    return FeatureBuildingSpawnResult.Failed(
                        $"the placement area in chunk {loaded.Position} is occupied or reserved by another entity.");
                }
                if (!loaded.TrySpawnRuntimeEntity(
                        building.entityArchetype.NodeData,
                        position,
                        out _))
                {
                    return FeatureBuildingSpawnResult.Failed(
                        $"chunk {loaded.Position} rejected the runtime entity for an unknown placement error.");
                }

                Debug.Log(
                    $"Spawned feature building '{building.persistentId}' in " +
                    $"loaded region {regionPosition}, chunk " +
                    $"{WorldPartition.WorldToChunk(position)}, at {position}.");
                return FeatureBuildingSpawnResult.Succeeded();
            }

            if (!HasPersistentTransform(building.entityArchetype.NodeData))
            {
                return FeatureBuildingSpawnResult.Failed(
                    $"'{building.persistentId}' needs a PersistentTransform " +
                    "component before it can be spawned into an unloaded chunk.");
            }

            Vector2Int chunkPosition =
                WorldPartition.WorldToChunk(position);
            ushort localIndex =
                WorldPartition.GetLocalChunkIndex(chunkPosition);
            ChunkState chunkState = region.TryGetChunkState(
                    localIndex,
                    out ChunkState existing)
                ? existing.CreateSnapshot()
                : new ChunkState { localChunkIndex = localIndex };
            NodeId entityId = NodeId.CreateRuntimeId();
            PersistentEntityRecord record = new()
            {
                id = entityId,
                archetypeId = building.entityArchetype.Id,
                persistenceKind = EntityPersistenceKind.RuntimeSpawned,
                existenceState = EntityExistenceState.Exists,
                lastSimulatedTick = _clock.CurrentTick
            };
            record.components.Add(CreateTransformState(position));
            chunkState.entities.Add(record);
            chunkState.lastSimulatedTick = _clock.CurrentTick;
            chunkState.Compact();
            if (!RegisterClimateInfluence(
                    entityId,
                    position,
                    building.entityArchetype.NodeData))
            {
                return FeatureBuildingSpawnResult.Failed(
                    $"another ClimateCore is already registered in region {regionPosition}.");
            }
            region.SetChunkState(localIndex, chunkState);
            _regions.MarkDirty(region);
            await _regions.FlushDirtyAsync();

            Debug.Log(
                $"Spawned feature building '{building.persistentId}' offline " +
                $"in region {regionPosition}, chunk {chunkPosition}, " +
                $"at {position}.");
            return FeatureBuildingSpawnResult.Succeeded();
        }

        private FeatureBuildingData FindBuilding(string buildingId)
        {
            string normalized = buildingId?.Trim();
            if (string.IsNullOrEmpty(normalized))
                return null;

            return _worldGeneration.AllFeatureBuildings.FirstOrDefault(
                building => string.Equals(
                    building.persistentId,
                    normalized,
                    StringComparison.OrdinalIgnoreCase));
        }

        public static Vector2 GetRuntimeSpawnPosition(
            Vector2Int region,
            uint worldSeed,
            string featureId,
            Vector2 placementOffset,
            long worldTick)
        {
            int regionSize =
                WorldPartition.RegionSizeInChunks *
                ChunkBuildResult.ChunkSize;
            int tickSalt = unchecked(
                (int)worldTick ^ (int)(worldTick >> 32));
            int salt = unchecked(
                (int)worldSeed ^
                StableHash(featureId) ^
                tickSalt);
            const float edgePadding = 1.5f;
            float usableSize = regionSize - edgePadding * 2f;
            Vector2 regionOrigin = new(
                region.x * regionSize,
                region.y * regionSize);
            Vector2 randomized = regionOrigin + new Vector2(
                edgePadding +
                Util.Hash01(region.x, region.y, salt ^ 0x51b3) *
                usableSize,
                edgePadding +
                Util.Hash01(region.x, region.y, salt ^ 0x7a91) *
                usableSize);
            Vector2 position = randomized + placementOffset;
            return new Vector2(
                Mathf.Clamp(
                    position.x,
                    regionOrigin.x + edgePadding,
                    regionOrigin.x + regionSize - edgePadding),
                Mathf.Clamp(
                    position.y,
                    regionOrigin.y + edgePadding,
                    regionOrigin.y + regionSize - edgePadding));
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char character in value ?? string.Empty)
                    hash = (hash ^ character) * 16777619u;
                return (int)hash;
            }
        }

        private async Awaitable<(bool found, Vector2 position)>
            TryLocateRuntimeBuildingAsync(
            FeatureBuildingData building,
            Vector2Int regionPosition)
        {
            RuntimeRegion region = await _regions.GetReadyAsync(
                regionPosition,
                _clock.CurrentTick);

            foreach (ChunkState chunk in region.ChangedChunks.Values)
            {
                foreach (PersistentEntityRecord entity in
                         chunk.entities ??
                         new List<PersistentEntityRecord>())
                {
                    if (entity == null ||
                        entity.existenceState != EntityExistenceState.Exists ||
                        entity.archetypeId != building.entityArchetype.Id ||
                        entity.components == null)
                    {
                        continue;
                    }

                    PersistenceComponentRecord transform =
                        entity.components.FirstOrDefault(component =>
                            component?.typeId == PersistentTransform.TypeId);
                    if (transform?.data == null ||
                        transform.data.Length < sizeof(float) * 2)
                    {
                        continue;
                    }

                    using MemoryStream stream =
                        new(transform.data, writable: false);
                    using BinaryReader reader = new(stream);
                    Vector2 position = new(
                        reader.ReadSingle(),
                        reader.ReadSingle());
                    return (true, position);
                }
            }

            return (false, default);
        }

        private static void LogLocatedFeature(
            string featureId,
            Vector2Int region,
            Vector2 position,
            string source)
        {
            Vector2Int chunk = WorldPartition.WorldToChunk(position);
            Debug.Log(
                $"Located {source} feature '{featureId}' in region {region}, " +
                $"chunk {chunk}, at {position}.");
        }

        private bool ContainsBuilding(
            RuntimeRegion region,
            FeatureBuildingData building)
        {
            HashSet<int> archetypes = _worldGeneration.FeatureBuildings
                .Where(candidate => string.Equals(
                    candidate.regionUniqueKey,
                    building.regionUniqueKey,
                    StringComparison.Ordinal))
                .Select(candidate => candidate.entityArchetype.Id)
                .ToHashSet();

            foreach (ChunkState chunk in region.ChangedChunks.Values)
            {
                foreach (PersistentEntityRecord entity in
                         chunk.entities ??
                         new List<PersistentEntityRecord>())
                {
                    if (entity != null &&
                        entity.existenceState == EntityExistenceState.Exists &&
                        archetypes.Contains(entity.archetypeId))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool TryFindRuntimeFeatureBuilding(
            RuntimeRegion region,
            out FeatureBuildingData building,
            out Vector2 position)
        {
            Dictionary<int, FeatureBuildingData> byArchetype =
                _worldGeneration.AllFeatureBuildings
                    .Where(candidate =>
                        candidate.entityArchetype != null)
                    .GroupBy(candidate => candidate.entityArchetype.Id)
                    .ToDictionary(group => group.Key, group => group.First());

            foreach (ChunkState chunk in region.ChangedChunks.Values)
            {
                foreach (PersistentEntityRecord entity in
                         chunk.entities ??
                         new List<PersistentEntityRecord>())
                {
                    if (entity == null ||
                        entity.existenceState != EntityExistenceState.Exists ||
                        !byArchetype.TryGetValue(
                            entity.archetypeId,
                            out building))
                    {
                        continue;
                    }

                    if (TryReadPosition(entity, out position))
                        return true;

                    Vector2Int local =
                        WorldPartition.GetLocalChunkCoordinate(
                            chunk.localChunkIndex);
                    Vector2Int globalChunk =
                        region.Position *
                        WorldPartition.RegionSizeInChunks +
                        local;
                    position = new Vector2(
                        globalChunk.x * ChunkBuildResult.ChunkSize,
                        globalChunk.y * ChunkBuildResult.ChunkSize);
                    return true;
                }
            }

            building = null;
            position = default;
            return false;
        }

        private static bool TryReadPosition(
            PersistentEntityRecord entity,
            out Vector2 position)
        {
            PersistenceComponentRecord transform =
                entity.components?.FirstOrDefault(component =>
                    component?.typeId == PersistentTransform.TypeId);
            if (transform?.data == null ||
                transform.data.Length < sizeof(float) * 2)
            {
                position = default;
                return false;
            }

            using MemoryStream stream =
                new(transform.data, writable: false);
            using BinaryReader reader = new(stream);
            position = new Vector2(
                reader.ReadSingle(),
                reader.ReadSingle());
            return true;
        }

        private static bool HasPersistentTransform(NodeData nodeData)
        {
            return nodeData?.persistentComponents?.Any(
                component => component is PersistentTransformData) == true;
        }

        private static PersistenceComponentRecord CreateTransformState(
            Vector2 position)
        {
            using MemoryStream stream = new();
            using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(position.x);
                writer.Write(position.y);
                writer.Write(0f);
                writer.Write(0f);
            }

            return new PersistenceComponentRecord
            {
                typeId = PersistentTransform.TypeId,
                version = 1,
                data = stream.ToArray()
            };
        }

        private bool RegisterClimateInfluence(
            NodeId entityId,
            Vector2 position,
            NodeData nodeData)
        {
            ClimateCoreData core = nodeData.persistentComponents?
                .OfType<ClimateCoreData>()
                .FirstOrDefault();
            if (core == null)
                return true;

            AreaOfEffectData area = nodeData.persistentComponents?
                .OfType<AreaOfEffectData>()
                .FirstOrDefault();
            float inner = area?.innerRadius ?? 0f;
            float outer = area?.outerRadius ?? 0f;
            return _climateCores.RegisterOrUpdate(new ClimateCoreInfluence(
                entityId,
                position,
                inner,
                outer,
                area?.spreadRate ?? 0f,
                inner,
                _clock.CurrentTick,
                core.temperatureOffset,
                core.permanentWeather?.WeatherId));
        }
    }
}
