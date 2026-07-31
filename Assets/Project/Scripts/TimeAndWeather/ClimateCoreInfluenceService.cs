using System;
using System.Collections.Generic;
using System.IO;
using Project.Scripts.Bus;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.TimeAndWeather
{
    public sealed class ClimateCoreInfluenceService :
        IClimateCoreInfluenceRegistry,
        IClimateCoreWeatherSource,
        IOfflineRegionSimulation,
        IInitializable,
        IDisposable
    {
        private const ushort RegionComponentTypeId = 0x4343;
        private const ushort RegionComponentVersion = 1;

        private readonly IWeatherWorldClock _clock;
        private readonly IRegionRepository _regions;
        private readonly MapSignalBus _mapSignals;
        private readonly Dictionary<NodeId, ClimateCoreInfluence> _cores = new();
        private readonly Dictionary<Vector2Int, NodeId> _coreByRegion = new();

        [Inject]
        public ClimateCoreInfluenceService(
            IWeatherWorldClock clock,
            IRegionRepository regions,
            MapSignalBus mapSignals)
        {
            _clock = clock;
            _regions = regions;
            _mapSignals = mapSignals;
        }

        public ClimateCoreInfluenceService(IWeatherWorldClock clock)
            : this(clock, null, null)
        {
        }

        public void Initialize()
        {
            if (_mapSignals != null)
                _mapSignals.ChunkLoaded += OnChunkLoaded;
        }

        public void Dispose()
        {
            if (_mapSignals != null)
                _mapSignals.ChunkLoaded -= OnChunkLoaded;
        }

        public bool RegisterOrUpdate(ClimateCoreInfluence influence)
        {
            Vector2Int region = WorldPartition.ChunkToRegion(
                WorldPartition.WorldToChunk(influence.Position));
            if (_coreByRegion.TryGetValue(region, out NodeId existing) &&
                !existing.Equals(influence.EntityId))
            {
                return false;
            }

            bool isNew = !_cores.ContainsKey(influence.EntityId);
            _cores[influence.EntityId] = influence;
            _coreByRegion[region] = influence.EntityId;
            if (isNew && _regions != null)
                PersistInfluenceAsync(influence);
            return true;
        }

        public void Remove(NodeId entityId)
        {
            if (!_cores.Remove(entityId, out ClimateCoreInfluence removed))
                return;

            Vector2Int region = WorldPartition.ChunkToRegion(
                WorldPartition.WorldToChunk(removed.Position));
            if (_coreByRegion.TryGetValue(region, out NodeId registered) &&
                registered.Equals(entityId))
            {
                _coreByRegion.Remove(region);
            }

            if (_regions != null)
                RemovePersistedInfluenceAsync(removed);
        }

        public void Apply(
            Vector2Int region,
            ClimateContext context,
            ref ClimateModifierAccumulator modifiers)
        {
            // Climate core temperature is intentionally not added to the
            // shared regional snapshot. It is spatial and must be sampled at
            // the requesting tile/world position by GetLocalSnapshot.
        }

        public float GetTemperatureOffset(Vector2 worldPosition)
        {
            long tick = _clock.CurrentTick;
            float offset = 0f;
            foreach (ClimateCoreInfluence core in _cores.Values)
            {
                float influence = EvaluateInfluence(core, worldPosition, tick);
                offset += core.TemperatureOffset * influence;
            }
            return offset;
        }

        public void HydrateRegion(RuntimeRegion region)
        {
            if (region == null)
                return;

            foreach (ClimateCoreInfluence influence in ReadInfluences(region))
                AddFromPersistence(influence);
        }

        public bool TryGetPermanentWeather(
            Vector2Int region,
            out string weatherId)
        {
            return TryGetPermanentWeather(
                region,
                _clock.CurrentTick,
                out weatherId);
        }

        public bool TryGetPermanentWeather(
            Vector2Int region,
            long tick,
            out string weatherId)
        {
            return TryGetPermanentWeather(
                region,
                tick,
                out weatherId,
                out _);
        }

        public bool TryGetPermanentWeather(
            Vector2Int region,
            long tick,
            out string weatherId,
            out float influence)
        {
            float regionSize =
                WorldPartition.RegionSizeInChunks *
                ChunkBuildResult.ChunkSize;
            float strongest = 0f;
            string selected = string.Empty;
            foreach (ClimateCoreInfluence core in _cores.Values)
            {
                if (string.IsNullOrWhiteSpace(core.PermanentWeatherId))
                    continue;

                float distance = DistanceToRegion(
                    core.Position,
                    region,
                    regionSize);
                float candidateInfluence = EvaluateInfluenceAtDistance(
                    core,
                    distance,
                    tick);
                if (candidateInfluence <= strongest)
                    continue;

                strongest = candidateInfluence;
                selected = core.PermanentWeatherId;
            }

            weatherId = selected;
            influence = strongest;
            return strongest > 0f;
        }

        public bool TryGetPermanentWeather(
            Vector2 worldPosition,
            long tick,
            out string weatherId,
            out float influence)
        {
            float strongest = 0f;
            string selected = string.Empty;
            foreach (ClimateCoreInfluence core in _cores.Values)
            {
                if (string.IsNullOrWhiteSpace(core.PermanentWeatherId))
                    continue;

                float candidate = EvaluateInfluence(
                    core,
                    worldPosition,
                    tick);
                if (candidate <= strongest)
                    continue;

                strongest = candidate;
                selected = core.PermanentWeatherId;
            }

            weatherId = selected;
            influence = strongest;
            return strongest > 0f;
        }

        public long GetNextUpdateTick(
            long currentTick,
            OfflineSimulationPolicy policy)
        {
            return checked(currentTick + 1);
        }

        public void Simulate(
            RuntimeRegion region,
            long fromTick,
            long toTick,
            OfflineSimulationPolicy policy)
        {
            if (region == null ||
                policy == OfflineSimulationPolicy.None ||
                toTick <= fromTick)
            {
                return;
            }

            // Region simulation runs before regional weather simulation. Load
            // the source entities first so unloaded cores still constrain
            // weather and temperature during the same offline catch-up pass.
            HydrateRegion(region);

            // Keep the original spread amount/tick anchor in each region. It
            // lets later simulations determine the exact tick at which the
            // expanding circle first reached this region.
        }

        public static float EvaluateInfluence(
            ClimateCoreInfluence core,
            Vector2 sample,
            long tick)
        {
            float distance = Vector2.Distance(core.Position, sample);
            return EvaluateInfluenceAtDistance(core, distance, tick);
        }

        private static float EvaluateInfluenceAtDistance(
            ClimateCoreInfluence core,
            float distance,
            long tick)
        {
            float outer = core.GetSpreadAt(tick);
            if (distance <= core.InnerRadius)
                return 1f;
            if (distance >= outer || outer <= core.InnerRadius)
                return 0f;

            float t = Mathf.InverseLerp(outer, core.InnerRadius, distance);
            return t * t * (3f - 2f * t);
        }

        public static Vector2 GetRegionCenter(Vector2Int region)
        {
            float size =
                WorldPartition.RegionSizeInChunks *
                ChunkBuildResult.ChunkSize;
            return new Vector2(
                (region.x + 0.5f) * size,
                (region.y + 0.5f) * size);
        }

        private async void OnChunkLoaded(Vector2Int chunkPosition, IChunk loaded)
        {
            try
            {
                Vector2Int regionPosition =
                    WorldPartition.ChunkToRegion(chunkPosition);
                RuntimeRegion region = await _regions.GetReadyAsync(
                    regionPosition,
                    _clock.CurrentTick);
                foreach (ClimateCoreInfluence influence in
                         ReadInfluences(region))
                {
                    AddFromPersistence(influence);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void AddFromPersistence(ClimateCoreInfluence influence)
        {
            Vector2Int ownerRegion = WorldPartition.ChunkToRegion(
                WorldPartition.WorldToChunk(influence.Position));
            if (_coreByRegion.TryGetValue(
                    ownerRegion,
                    out NodeId existing) &&
                !existing.Equals(influence.EntityId))
            {
                return;
            }

            _cores[influence.EntityId] = influence;
            _coreByRegion[ownerRegion] = influence.EntityId;
        }

        private async void PersistInfluenceAsync(
            ClimateCoreInfluence influence)
        {
            try
            {
                foreach (Vector2Int position in EnumerateAffectedRegions(
                             influence))
                {
                    RuntimeRegion region = await _regions.GetReadyAsync(
                        position,
                        _clock.CurrentTick);
                    List<ClimateCoreInfluence> records =
                        ReadInfluences(region);
                    int index = records.FindIndex(candidate =>
                        candidate.EntityId.Equals(influence.EntityId));
                    if (index >= 0)
                        records[index] = influence;
                    else
                        records.Add(influence);
                    WriteInfluences(region, records);
                    _regions.MarkDirty(region);
                }

                // A ClimateCore may unload immediately after it is spawned.
                // Checkpoint its affected-region references now instead of
                // relying on a later chunk-unload save winning this async race.
                await _regions.FlushDirtyAsync();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private async void RemovePersistedInfluenceAsync(
            ClimateCoreInfluence influence)
        {
            try
            {
                foreach (Vector2Int position in EnumerateAffectedRegions(
                             influence))
                {
                    RuntimeRegion region = await _regions.GetReadyAsync(
                        position,
                        _clock.CurrentTick);
                    List<ClimateCoreInfluence> records =
                        ReadInfluences(region);
                    if (records.RemoveAll(candidate =>
                            candidate.EntityId.Equals(influence.EntityId)) == 0)
                    {
                        continue;
                    }

                    WriteInfluences(region, records);
                    _regions.MarkDirty(region);
                }

                await _regions.FlushDirtyAsync();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static IEnumerable<Vector2Int> EnumerateAffectedRegions(
            ClimateCoreInfluence influence)
        {
            float regionSize =
                WorldPartition.RegionSizeInChunks *
                ChunkBuildResult.ChunkSize;
            int minimumX = Mathf.FloorToInt(
                (influence.Position.x - influence.OuterRadius) / regionSize);
            int maximumX = Mathf.FloorToInt(
                (influence.Position.x + influence.OuterRadius) / regionSize);
            int minimumY = Mathf.FloorToInt(
                (influence.Position.y - influence.OuterRadius) / regionSize);
            int maximumY = Mathf.FloorToInt(
                (influence.Position.y + influence.OuterRadius) / regionSize);

            for (int y = minimumY; y <= maximumY; y++)
            {
                for (int x = minimumX; x <= maximumX; x++)
                {
                    Vector2Int region = new(x, y);
                    if (DistanceToRegion(
                            influence.Position,
                            region,
                            regionSize) <= influence.OuterRadius)
                    {
                        yield return region;
                    }
                }
            }
        }

        private static float DistanceToRegion(
            Vector2 point,
            Vector2Int region,
            float size)
        {
            float minimumX = region.x * size;
            float minimumY = region.y * size;
            float closestX = Mathf.Clamp(
                point.x,
                minimumX,
                minimumX + size);
            float closestY = Mathf.Clamp(
                point.y,
                minimumY,
                minimumY + size);
            return Vector2.Distance(point, new Vector2(closestX, closestY));
        }

        private static List<ClimateCoreInfluence> ReadInfluences(
            RuntimeRegion region)
        {
            List<ClimateCoreInfluence> influences = new();
            if (!region.Components.TryGetValue(
                    RegionComponentTypeId,
                    out RegionComponentRecord component) ||
                component?.data == null ||
                component.data.Length == 0)
            {
                return influences;
            }
            if (component.version != RegionComponentVersion)
            {
                Debug.LogWarning(
                    $"Unsupported climate-core region component version " +
                    $"{component.version} in {region.Position}.");
                return influences;
            }

            using MemoryStream stream =
                new(component.data, writable: false);
            using BinaryReader reader = new(stream);
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                influences.Add(new ClimateCoreInfluence(
                    new NodeId(reader.ReadUInt64()),
                    new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadInt64(),
                    reader.ReadSingle(),
                    reader.ReadString()));
            }
            return influences;
        }

        private static void WriteInfluences(
            RuntimeRegion region,
            List<ClimateCoreInfluence> influences)
        {
            using MemoryStream stream = new();
            using (BinaryWriter writer =
                   new(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(influences.Count);
                foreach (ClimateCoreInfluence influence in influences)
                {
                    writer.Write(influence.EntityId.value);
                    writer.Write(influence.Position.x);
                    writer.Write(influence.Position.y);
                    writer.Write(influence.InnerRadius);
                    writer.Write(influence.OuterRadius);
                    writer.Write(influence.SpreadRate);
                    writer.Write(influence.SpreadAmount);
                    writer.Write(influence.AtTick);
                    writer.Write(influence.TemperatureOffset);
                    writer.Write(influence.PermanentWeatherId);
                }
            }

            region.SetComponent(new RegionComponentRecord
            {
                typeId = RegionComponentTypeId,
                version = RegionComponentVersion,
                data = stream.ToArray(),
                isAtBaseline = influences.Count == 0
            });
        }
    }
}
