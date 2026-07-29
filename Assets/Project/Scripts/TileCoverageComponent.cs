using System;
using System.Collections.Generic;
using System.IO;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using Project.Scripts.TimeAndWeather;
using UnityEngine;

namespace Project.Scripts
{
    public sealed class TileCoverageComponent :
        MonoBehaviour,
        IPersistentComponent
    {
        public const ushort TypeId = 0x4356; // "CV"
        private const ushort Version = 1;
        private const int MaximumSavedLayers = 1024;
        private const int MaximumSavedCells =
            ChunkBuildResult.ChunkSize * ChunkBuildResult.ChunkSize;

        private sealed class CellState
        {
            public ushort LocalIndex;
            public float TerrainTemperature;
            public float Amount;
            public bool Initialized;
        }

        private sealed class LayerState
        {
            public CoverageData Data;
            public readonly Dictionary<ushort, CellState> Cells = new();
        }

        private readonly Dictionary<string, LayerState> _layers =
            new(StringComparer.Ordinal);
        private readonly CoverageData[] _displayedData =
            new CoverageData[MaximumSavedCells];
        private readonly byte[] _displayedAlpha =
            new byte[MaximumSavedCells];

        private Chunk _chunk;
        private bool _ready;

        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => Version;
        public bool IsReady => _ready;

        public void Configure(
            Chunk chunk,
            ChunkBuildResult build,
            float waterHeight,
            IReadOnlyList<CoverageData> coverageLayers)
        {
            _chunk = chunk;
            _ready = false;
            _layers.Clear();
            Array.Clear(_displayedData, 0, _displayedData.Length);
            Array.Clear(_displayedAlpha, 0, _displayedAlpha.Length);

            for (ushort index = 0; index < MaximumSavedCells; index++)
            {
                if (build.heights[index] <= waterHeight)
                    continue;

                for (int coverageIndex = 0;
                     coverageIndex < (coverageLayers?.Count ?? 0);
                     coverageIndex++)
                {
                    CoverageData data = coverageLayers[coverageIndex];
                    if (data == null ||
                        string.IsNullOrWhiteSpace(data.CoverageId))
                    {
                        continue;
                    }

                    if (!_layers.TryGetValue(
                            data.CoverageId,
                            out LayerState layer))
                    {
                        layer = new LayerState { Data = data };
                        _layers.Add(data.CoverageId, layer);
                    }
                    else if (layer.Data != data)
                    {
                        Debug.LogError(
                            $"Coverage ID '{data.CoverageId}' is used by multiple assets.",
                            data);
                        continue;
                    }

                    layer.Cells.TryAdd(index, new CellState
                    {
                        LocalIndex = index,
                        TerrainTemperature = build.temperature[index]
                    });
                }
            }
        }

        public void CompleteRestore(WeatherSample regionalWeather)
        {
            foreach (LayerState layer in _layers.Values)
            {
                foreach (CellState cell in layer.Cells.Values)
                {
                    if (cell.Initialized)
                        continue;

                    WeatherSample sample =
                        regionalWeather.WithTerrainTemperature(
                            cell.TerrainTemperature);
                    if (TemperatureAllowsPersistence(layer.Data, sample))
                    {
                        cell.Amount = _chunk.TryGetNeighborCoverageSeed(
                            cell.LocalIndex,
                            layer.Data,
                            out float neighborAmount)
                            ? neighborAmount
                            : WeatherAllowsAccumulation(layer.Data, sample)
                                ? layer.Data.InitialCoverage
                                : 0f;
                    }
                    else
                    {
                        cell.Amount = 0f;
                    }
                    cell.Initialized = true;
                }
            }

            _ready = true;
            RefreshAllVisuals(force: true);
        }

        public void Advance(
            WeatherSample regionalWeather,
            long elapsedTicks)
        {
            if (!_ready || elapsedTicks <= 0)
                return;

            float ticks = Mathf.Min(elapsedTicks, int.MaxValue);
            HashSet<ushort> dirtyCells = new();

            foreach (LayerState layer in _layers.Values)
            {
                foreach (CellState cell in layer.Cells.Values)
                {
                    WeatherSample sample =
                        regionalWeather.WithTerrainTemperature(
                            cell.TerrainTemperature);
                    float previous = cell.Amount;
                    if (!TemperatureAllowsPersistence(layer.Data, sample))
                    {
                        cell.Amount = layer.Data.SlowlyDecayWhenTemperatureFails
                            ? Mathf.Clamp01(
                                cell.Amount -
                                layer.Data.DecayRate * ticks)
                            : 0f;
                    }
                    else if (WeatherAllowsAccumulation(layer.Data, sample))
                    {
                        float accumulationMultiplier =
                            GetAccumulationMultiplier(
                                layer.Data,
                                cell.LocalIndex);
                        cell.Amount = Mathf.Clamp01(
                            cell.Amount +
                            layer.Data.AccumulationRate *
                            accumulationMultiplier *
                            ticks);
                    }

                    if (!Mathf.Approximately(previous, cell.Amount))
                        dirtyCells.Add(cell.LocalIndex);
                }
            }

            foreach (ushort index in dirtyCells)
                RefreshVisual(index, force: false);

            if (dirtyCells.Count > 0)
                RefreshMaterialProperties();
        }

        private float GetAccumulationMultiplier(
            CoverageData data,
            ushort localIndex)
        {
            float variation = data.AccumulationRateVariation;
            if (variation <= 0f)
                return 1f;

            int size = ChunkBuildResult.ChunkSize;
            int worldX = _chunk.Position.x * size + localIndex % size;
            int worldY = _chunk.Position.y * size + localIndex / size;
            uint hash = 2166136261u;
            unchecked
            {
                hash = (hash ^ (uint)worldX) * 16777619u;
                hash = (hash ^ (uint)worldY) * 16777619u;
                foreach (char character in data.CoverageId)
                    hash = (hash ^ character) * 16777619u;

                hash ^= hash >> 16;
                hash *= 0x7feb352du;
                hash ^= hash >> 15;
                hash *= 0x846ca68bu;
                hash ^= hash >> 16;
            }

            float normalized = (hash & 0x00ffffffu) / 16777215f;
            return Mathf.Lerp(1f - variation, 1f + variation, normalized);
        }

        public bool TryGetCoverage(
            ushort localIndex,
            CoverageData coverage,
            out float amount)
        {
            amount = 0f;
            if (!_ready ||
                coverage == null ||
                !_layers.TryGetValue(
                    coverage.CoverageId,
                    out LayerState layer) ||
                !layer.Cells.TryGetValue(
                    localIndex,
                    out CellState cell))
            {
                return false;
            }

            amount = cell.Amount;
            return true;
        }

        public bool TrySetCoverage(
            ushort localIndex,
            CoverageData coverage,
            float amount)
        {
            if (!_ready ||
                coverage == null ||
                !_layers.TryGetValue(
                    coverage.CoverageId,
                    out LayerState layer) ||
                !layer.Cells.TryGetValue(
                    localIndex,
                    out CellState cell))
            {
                return false;
            }

            cell.Amount = Mathf.Clamp01(amount);
            cell.Initialized = true;
            RefreshVisual(localIndex, force: true);
            RefreshMaterialProperties();
            return true;
        }

        public void RefreshCell(ushort localIndex) =>
            RefreshVisual(localIndex, force: true);

        public void PrepareForPool()
        {
            _ready = false;
            _layers.Clear();
            _chunk?.ClearCoverageVisuals();
            _chunk = null;
        }

        public void WriteState(BinaryWriter writer)
        {
            writer.Write(_layers.Count);
            List<string> coverageIds = new(_layers.Keys);
            coverageIds.Sort(StringComparer.Ordinal);
            foreach (string coverageId in coverageIds)
            {
                LayerState layer = _layers[coverageId];
                writer.Write(coverageId);
                writer.Write(layer.Cells.Count);
                List<ushort> cellIndices = new(layer.Cells.Keys);
                cellIndices.Sort();
                foreach (ushort cellIndex in cellIndices)
                {
                    CellState cell = layer.Cells[cellIndex];
                    writer.Write(cell.LocalIndex);
                    writer.Write(Mathf.Clamp01(cell.Amount));
                }
            }
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            if (savedVersion != Version)
            {
                throw new InvalidDataException(
                    $"Unsupported tile coverage version {savedVersion}.");
            }

            int layerCount = reader.ReadInt32();
            if (layerCount < 0 || layerCount > MaximumSavedLayers)
                throw new InvalidDataException("Invalid coverage layer count.");

            for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                string coverageId = reader.ReadString();
                int cellCount = reader.ReadInt32();
                if (cellCount < 0 || cellCount > MaximumSavedCells)
                    throw new InvalidDataException("Invalid coverage cell count.");

                _layers.TryGetValue(coverageId, out LayerState layer);
                for (int cellIndex = 0; cellIndex < cellCount; cellIndex++)
                {
                    ushort localIndex = reader.ReadUInt16();
                    float amount = reader.ReadSingle();
                    if (localIndex >= MaximumSavedCells ||
                        float.IsNaN(amount) ||
                        float.IsInfinity(amount))
                    {
                        throw new InvalidDataException(
                            "Invalid saved tile coverage value.");
                    }

                    if (layer != null &&
                        layer.Cells.TryGetValue(
                            localIndex,
                            out CellState cell))
                    {
                        cell.Amount = Mathf.Clamp01(amount);
                        cell.Initialized = true;
                    }
                }
            }
        }

        public bool IsAtBaseline() => _layers.Count == 0;

        public static bool ConditionsAllow(
            CoverageData data,
            WeatherSample sample)
        {
            return TemperatureAllowsPersistence(data, sample) &&
                   WeatherAllowsAccumulation(data, sample);
        }

        public static bool TemperatureAllowsPersistence(
            CoverageData data,
            WeatherSample sample)
        {
            if (data == null)
                return false;

            Vector2 range = data.AmbientTemperatureRange;
            float minimum = Mathf.Min(range.x, range.y);
            float maximum = Mathf.Max(range.x, range.y);
            return sample.AmbientTemperature >= minimum &&
                   sample.AmbientTemperature <= maximum;
        }

        public static bool WeatherAllowsAccumulation(
            CoverageData data,
            WeatherSample sample)
        {
            if (data == null ||
                !MatchesId(data.AllowedWeatherIds, sample.WeatherId) ||
                !MatchesId(data.AllowedPhaseIds, sample.PhaseId))
            {
                return false;
            }

            string[] requiredEffects = data.RequiredActiveEffectIds;
            if (requiredEffects.Length == 0)
                return true;

            bool hasRestriction = false;
            foreach (string requiredId in requiredEffects)
            {
                if (!string.IsNullOrWhiteSpace(requiredId))
                {
                    hasRestriction = true;
                    break;
                }
            }
            if (!hasRestriction)
                return true;
            if (sample.ActiveEffects == null)
                return false;

            foreach (string requiredId in requiredEffects)
            {
                if (string.IsNullOrWhiteSpace(requiredId))
                    continue;

                foreach (WeatherEffectSample active in sample.ActiveEffects)
                {
                    if (active.Effect != null &&
                        active.Intensity > 0f &&
                        string.Equals(
                            requiredId,
                            active.Effect.EffectId,
                            StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool MatchesId(
            string[] allowedIds,
            string currentId)
        {
            if (allowedIds == null || allowedIds.Length == 0)
                return true;

            bool hasRestriction = false;
            foreach (string allowed in allowedIds)
            {
                if (string.IsNullOrWhiteSpace(allowed))
                    continue;

                hasRestriction = true;
                if (string.Equals(
                        allowed,
                        currentId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return !hasRestriction;
        }

        private void RefreshAllVisuals(bool force)
        {
            for (ushort index = 0; index < MaximumSavedCells; index++)
                RefreshVisual(index, force);

            RefreshMaterialProperties();
        }

        private void RefreshVisual(ushort localIndex, bool force)
        {
            CoverageData winner = null;
            float winnerAmount = 0f;
            foreach (LayerState layer in _layers.Values)
            {
                if (!layer.Cells.TryGetValue(
                        localIndex,
                        out CellState cell) ||
                    cell.Amount <= 0f)
                {
                    continue;
                }

                if (winner == null ||
                    layer.Data.RenderPriority > winner.RenderPriority ||
                    (layer.Data.RenderPriority == winner.RenderPriority &&
                     string.CompareOrdinal(
                         layer.Data.CoverageId,
                         winner.CoverageId) < 0))
                {
                    winner = layer.Data;
                    winnerAmount = cell.Amount;
                }
            }

            byte alpha = (byte)Mathf.RoundToInt(
                Mathf.Clamp01(winnerAmount) * 255f);
            if (!force &&
                _displayedData[localIndex] == winner &&
                _displayedAlpha[localIndex] == alpha)
            {
                return;
            }

            _displayedData[localIndex] = winner;
            _displayedAlpha[localIndex] = alpha;
            _chunk?.ApplyCoverageVisual(
                localIndex,
                winner,
                alpha / 255f);
        }

        private void RefreshMaterialProperties()
        {
            CoverageData winner = null;
            float total = 0f;
            int count = 0;

            foreach (LayerState layer in _layers.Values)
            {
                float layerTotal = 0f;
                foreach (CellState cell in layer.Cells.Values)
                {
                    layerTotal += cell.Amount;
                }

                if (layerTotal <= 0f)
                    continue;

                if (winner == null ||
                    layer.Data.RenderPriority > winner.RenderPriority ||
                    (layer.Data.RenderPriority == winner.RenderPriority &&
                     string.CompareOrdinal(
                         layer.Data.CoverageId,
                         winner.CoverageId) < 0))
                {
                    winner = layer.Data;
                    total = layerTotal;
                    count = layer.Cells.Count;
                }
            }

            _chunk?.ApplyCoverageMaterial(
                winner,
                count == 0 ? 0f : total / count);
        }
    }
}
