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
            public float AccumulationMultiplier = 1f;
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
        private readonly bool[] _indoorCells =
            new bool[MaximumSavedCells];
        private readonly bool[] _dirtyCellFlags =
            new bool[MaximumSavedCells];
        private readonly ushort[] _dirtyCells =
            new ushort[MaximumSavedCells];
        private readonly ushort[] _visualChanges =
            new ushort[MaximumSavedCells];

        private Chunk _chunk;
        private bool _ready;
        private int _generation;
        private CoverageData _displayedMaterialData;

        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => Version;
        public bool IsReady => _ready;
        public int Generation => _generation;

        public void Configure(
            Chunk chunk,
            ChunkBuildResult build,
            float waterHeight,
            IReadOnlyList<CoverageData> coverageLayers)
        {
            unchecked
            {
                _generation++;
            }
            _chunk = chunk;
            _ready = false;
            _displayedMaterialData = null;
            _layers.Clear();
            Array.Clear(_displayedData, 0, _displayedData.Length);
            Array.Clear(_displayedAlpha, 0, _displayedAlpha.Length);
            Array.Clear(_indoorCells, 0, _indoorCells.Length);

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
                        TerrainTemperature = build.temperature[index],
                        AccumulationMultiplier =
                            CalculateAccumulationMultiplier(data, index)
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

                    if (!TileAllowsCoverage(layer.Data, cell.LocalIndex))
                    {
                        cell.Amount = 0f;
                        cell.Initialized = true;
                        continue;
                    }

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
                            : AllowsWeatherAccumulation(
                                _indoorCells[cell.LocalIndex],
                                WeatherAllowsAccumulation(layer.Data, sample))
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
            int dirtyCount = 0;

            foreach (LayerState layer in _layers.Values)
            {
                bool accumulationAllowed =
                    WeatherAllowsAccumulation(layer.Data, regionalWeather);
                foreach (CellState cell in layer.Cells.Values)
                {
                    if (!TileAllowsCoverage(layer.Data, cell.LocalIndex))
                        continue;

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
                    else if (AllowsWeatherAccumulation(
                                 _indoorCells[cell.LocalIndex],
                                 accumulationAllowed))
                    {
                        cell.Amount = Mathf.Clamp01(
                            cell.Amount +
                            layer.Data.AccumulationRate *
                            cell.AccumulationMultiplier *
                            ticks);
                    }

                    if (!Mathf.Approximately(previous, cell.Amount) &&
                        !_dirtyCellFlags[cell.LocalIndex])
                    {
                        _dirtyCellFlags[cell.LocalIndex] = true;
                        _dirtyCells[dirtyCount++] = cell.LocalIndex;
                    }
                }
            }

            int visualCount = 0;
            for (int i = 0; i < dirtyCount; i++)
            {
                ushort index = _dirtyCells[i];
                _dirtyCellFlags[index] = false;
                if (UpdateDisplayedVisual(index, force: false))
                    _visualChanges[visualCount++] = index;
            }

            if (visualCount > 0)
                _chunk?.ApplyCoverageVisuals(
                    _visualChanges,
                    visualCount,
                    _displayedData,
                    _displayedAlpha);

            if (dirtyCount > 0)
                RefreshMaterialProperties();
        }

        private float CalculateAccumulationMultiplier(
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
                !TileAllowsCoverage(coverage, localIndex) ||
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
                !TileAllowsCoverage(coverage, localIndex) ||
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

        public bool HasCoverage(ushort localIndex)
        {
            return TryGetDisplayedCoverage(
                localIndex,
                out _,
                out _);
        }

        public bool TryReduceCoverage(
            ushort localIndex,
            float amount)
        {
            if (amount <= 0f ||
                !TryGetDisplayedCoverage(
                    localIndex,
                    out LayerState layer,
                    out CellState cell))
            {
                return false;
            }

            cell.Amount = Mathf.Max(0f, cell.Amount - amount);
            cell.Initialized = true;
            RefreshVisual(localIndex, force: true);
            RefreshMaterialProperties();
            return true;
        }

        public void RefreshCell(ushort localIndex)
        {
            RefreshVisual(localIndex, force: true);
            RefreshMaterialProperties();
        }

        public void ClearCell(ushort localIndex)
        {
            bool found = false;
            foreach (LayerState layer in _layers.Values)
            {
                if (!layer.Cells.TryGetValue(
                        localIndex,
                        out CellState cell))
                {
                    continue;
                }

                cell.Amount = 0f;
                cell.Initialized = true;
                found = true;
            }

            if (found && _ready)
                RefreshCell(localIndex);
        }

        public void SetRoomInteriorCells(
            IReadOnlyList<ushort> localIndices,
            bool isInterior)
        {
            if (localIndices == null || localIndices.Count == 0)
                return;

            int visualCount = 0;
            bool amountChanged = false;
            for (int i = 0; i < localIndices.Count; i++)
            {
                ushort localIndex = localIndices[i];
                if (localIndex >= MaximumSavedCells ||
                    _indoorCells[localIndex] == isInterior)
                {
                    continue;
                }

                _indoorCells[localIndex] = isInterior;
                if (!isInterior)
                    continue;

                foreach (LayerState layer in _layers.Values)
                {
                    if (!layer.Cells.TryGetValue(
                            localIndex,
                            out CellState cell) ||
                        cell.Amount <= 0f)
                    {
                        continue;
                    }

                    cell.Amount = 0f;
                    cell.Initialized = true;
                    amountChanged = true;
                }

                if (_ready &&
                    UpdateDisplayedVisual(localIndex, force: false))
                {
                    _visualChanges[visualCount++] = localIndex;
                }
            }

            if (visualCount > 0)
            {
                _chunk?.ApplyCoverageVisuals(
                    _visualChanges,
                    visualCount,
                    _displayedData,
                    _displayedAlpha);
            }

            if (amountChanged)
                RefreshMaterialProperties();
        }

        internal static bool AllowsWeatherAccumulation(
            bool isRoomInterior,
            bool weatherAllowsAccumulation) =>
            !isRoomInterior && weatherAllowsAccumulation;

        public void RefreshCellColor(ushort localIndex) =>
            RefreshVisual(localIndex, force: true);

        private bool TileAllowsCoverage(
            CoverageData data,
            ushort localIndex)
        {
            return _chunk != null &&
                   _chunk.TryGetCoverageGroundTile(
                       localIndex,
                       out TileData tile) &&
                   data.AllowsTile(tile);
        }

        public void PrepareForPool()
        {
            unchecked
            {
                _generation++;
            }
            _ready = false;
            _displayedMaterialData = null;
            _layers.Clear();
            Array.Clear(_indoorCells, 0, _indoorCells.Length);
            _chunk?.ClearCoverageVisuals();
            _chunk = null;
        }

        public void WriteState(BinaryWriter writer)
        {
            List<string> coverageIds = new(_layers.Keys);
            coverageIds.Sort(StringComparer.Ordinal);
            int savedLayerCount = 0;
            foreach (string coverageId in coverageIds)
            {
                if (CountSavedCells(_layers[coverageId]) > 0)
                    savedLayerCount++;
            }

            writer.Write(savedLayerCount);
            foreach (string coverageId in coverageIds)
            {
                LayerState layer = _layers[coverageId];
                int savedCellCount = CountSavedCells(layer);
                if (savedCellCount == 0)
                    continue;

                writer.Write(coverageId);
                writer.Write(savedCellCount);
                List<ushort> cellIndices = new(layer.Cells.Keys);
                cellIndices.Sort();
                foreach (ushort cellIndex in cellIndices)
                {
                    CellState cell = layer.Cells[cellIndex];
                    if (cell.Amount <= 0f)
                        continue;

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
                if (layer != null)
                {
                    // A persisted layer uses zero as its sparse default. Mark
                    // omitted cells initialized so CompleteRestore does not
                    // regenerate them from current weather or neighboring chunks.
                    foreach (CellState cell in layer.Cells.Values)
                    {
                        cell.Amount = 0f;
                        cell.Initialized = true;
                    }
                }

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

        public bool IsAtBaseline()
        {
            foreach (LayerState layer in _layers.Values)
            {
                if (CountSavedCells(layer) > 0)
                    return false;
            }

            return true;
        }

        private static int CountSavedCells(LayerState layer)
        {
            int count = 0;
            foreach (CellState cell in layer.Cells.Values)
            {
                if (cell.Amount > 0f)
                    count++;
            }

            return count;
        }

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
            int visualCount = 0;
            for (ushort index = 0; index < MaximumSavedCells; index++)
            {
                if (UpdateDisplayedVisual(index, force))
                    _visualChanges[visualCount++] = index;
            }

            if (visualCount > 0)
                _chunk?.ApplyCoverageVisuals(
                    _visualChanges,
                    visualCount,
                    _displayedData,
                    _displayedAlpha);

            RefreshMaterialProperties(force: true);
        }

        private void RefreshVisual(ushort localIndex, bool force)
        {
            if (!UpdateDisplayedVisual(localIndex, force))
                return;

            _visualChanges[0] = localIndex;
            _chunk?.ApplyCoverageVisuals(
                _visualChanges,
                1,
                _displayedData,
                _displayedAlpha);
        }

        private bool UpdateDisplayedVisual(ushort localIndex, bool force)
        {
            bool hasWinner = TryGetDisplayedCoverage(
                localIndex,
                out LayerState winningLayer,
                out CellState winningCell);
            CoverageData winner = hasWinner ? winningLayer.Data : null;
            float winnerAmount = hasWinner ? winningCell.Amount : 0f;

            byte alpha = (byte)Mathf.RoundToInt(
                Mathf.Clamp01(winnerAmount) * 255f);
            if (!force &&
                _displayedData[localIndex] == winner &&
                _displayedAlpha[localIndex] == alpha)
            {
                return false;
            }

            _displayedData[localIndex] = winner;
            _displayedAlpha[localIndex] = alpha;
            return true;
        }

        private bool TryGetDisplayedCoverage(
            ushort localIndex,
            out LayerState winningLayer,
            out CellState winningCell)
        {
            winningLayer = null;
            winningCell = null;
            if (!_ready || localIndex >= MaximumSavedCells)
                return false;

            foreach (LayerState layer in _layers.Values)
            {
                if (!layer.Cells.TryGetValue(
                        localIndex,
                        out CellState cell) ||
                    cell.Amount <= 0f ||
                    !TileAllowsCoverage(layer.Data, localIndex))
                {
                    continue;
                }

                if (winningLayer == null ||
                    layer.Data.RenderPriority >
                    winningLayer.Data.RenderPriority ||
                    (layer.Data.RenderPriority ==
                     winningLayer.Data.RenderPriority &&
                     string.CompareOrdinal(
                         layer.Data.CoverageId,
                         winningLayer.Data.CoverageId) < 0))
                {
                    winningLayer = layer;
                    winningCell = cell;
                }
            }

            return winningLayer != null;
        }

        private void RefreshMaterialProperties(bool force = false)
        {
            CoverageData winner = null;

            foreach (LayerState layer in _layers.Values)
            {
                bool hasCoverage = false;
                foreach (CellState cell in layer.Cells.Values)
                {
                    if (cell.Amount <= 0f ||
                        !TileAllowsCoverage(
                            layer.Data,
                            cell.LocalIndex))
                    {
                        continue;
                    }

                    hasCoverage = true;
                    break;
                }

                if (!hasCoverage)
                    continue;

                if (winner == null ||
                    layer.Data.RenderPriority > winner.RenderPriority ||
                    (layer.Data.RenderPriority == winner.RenderPriority &&
                     string.CompareOrdinal(
                         layer.Data.CoverageId,
                         winner.CoverageId) < 0))
                {
                    winner = layer.Data;
                }
            }

            if (!force && winner == _displayedMaterialData)
                return;

            _displayedMaterialData = winner;
            _chunk?.ApplyCoverageMaterial(winner, 0f);
        }
    }
}
