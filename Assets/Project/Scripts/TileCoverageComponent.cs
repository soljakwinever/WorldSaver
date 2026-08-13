using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using Project.Scripts.TimeAndWeather;
using Unity.Profiling;
using UnityEngine;

namespace Project.Scripts
{
    public sealed class TileCoverageComponent :
        MonoBehaviour,
        IPersistentComponent,
        ITownTileRepairSource
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

            public void Reset(
                ushort localIndex,
                float terrainTemperature,
                float accumulationMultiplier)
            {
                LocalIndex = localIndex;
                TerrainTemperature = terrainTemperature;
                AccumulationMultiplier = accumulationMultiplier;
                Amount = 0f;
                Initialized = false;
            }
        }

        private sealed class LayerState
        {
            public CoverageData Data;
            public bool VisibleCoverageDirty = true;
            public bool HasVisibleCoverage;
            public readonly Dictionary<ushort, CellState> Cells =
                new(MaximumSavedCells);
            private readonly CellState[] _cellPool =
                new CellState[MaximumSavedCells];
            private readonly byte[] _tileEligibility =
                new byte[MaximumSavedCells];

            public CellState AcquireCell(
                ushort localIndex,
                float terrainTemperature,
                float accumulationMultiplier)
            {
                if (Cells.TryGetValue(localIndex, out CellState existing))
                    return existing;

                CellState cell = _cellPool[localIndex] ??= new CellState();
                cell.Reset(
                    localIndex,
                    terrainTemperature,
                    accumulationMultiplier);
                Cells.Add(localIndex, cell);
                return cell;
            }

            public void PrepareForPool()
            {
                Data = null;
                Cells.Clear();
                VisibleCoverageDirty = true;
                HasVisibleCoverage = false;
                Array.Clear(_tileEligibility, 0, _tileEligibility.Length);
            }

            public bool TryGetTileEligibility(
                ushort localIndex,
                out bool eligible)
            {
                byte cached = _tileEligibility[localIndex];
                eligible = cached == 2;
                return cached != 0;
            }

            public void SetTileEligibility(ushort localIndex, bool eligible) =>
                _tileEligibility[localIndex] = eligible ? (byte)2 : (byte)1;

            public void InvalidateTileEligibility(ushort localIndex) =>
                _tileEligibility[localIndex] = 0;
        }

        private readonly Dictionary<string, LayerState> _layers =
            new(StringComparer.Ordinal);
        private readonly Stack<LayerState> _layerPool = new();
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
        private readonly Coroutine[] _coverageFadeRoutines =
            new Coroutine[MaximumSavedCells];
        private readonly float[] _animatedCoverageAmounts =
            new float[MaximumSavedCells];
        private readonly WeatherSample[] _weatherSamples =
            new WeatherSample[MaximumSavedCells];
        private readonly bool[] _weatherSampled =
            new bool[MaximumSavedCells];
        private readonly ushort[] _sampledWeatherCells =
            new ushort[MaximumSavedCells];

        private static readonly ProfilerMarker SimulationMarker =
            new("TileCoverage.SimulateCells");
        private static readonly ProfilerMarker VisualMarker =
            new("TileCoverage.ResolveVisuals");
        private static readonly ProfilerMarker RestoreMarker =
            new("Chunk.Restore.CoverageCells");

        [SerializeField, Min(0f)]
        [Tooltip("Seconds taken for mined coverage to recede through the accumulation shader.")]
        private float coverageMiningFadeDuration = 0.25f;

        private Chunk _chunk;
        private bool _ready;
        private int _generation;
        private CoverageData _displayedMaterialData;

        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => Version;
        public bool IsReady => _ready;
        public int Generation => _generation;

        private void OnEnable()
        {
            TownTileRepairRegistry.Register(this);
        }

        private void OnDisable()
        {
            TownTileRepairRegistry.Unregister(this);
        }

        public int GetMaximumMissingHealth(
            Vector2 townCenter,
            float townRadius) =>
            _chunk == null
                ? 0
                : _chunk.GetMaximumDamagedWallHealth(
                    townCenter,
                    townRadius);

        public int RepairDamagedTiles(
            Vector2 townCenter,
            float townRadius,
            int healthPerTile) =>
            _chunk == null
                ? 0
                : _chunk.RepairDamagedWalls(
                    townCenter,
                    townRadius,
                    healthPerTile);

        public void Configure(
            Chunk chunk,
            ChunkBuildResult build,
            float waterHeight,
            IReadOnlyList<CoverageData> coverageLayers)
        {
            CancelAllCoverageFades();
            unchecked
            {
                _generation++;
            }
            _chunk = chunk;
            _ready = false;
            _displayedMaterialData = null;
            RecycleLayers();
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
                        layer = _layerPool.Count > 0
                            ? _layerPool.Pop()
                            : new LayerState();
                        layer.Data = data;
                        _layers.Add(data.CoverageId, layer);
                    }
                    else if (layer.Data != data)
                    {
                        Debug.LogError(
                            $"Coverage ID '{data.CoverageId}' is used by multiple assets.",
                            data);
                        continue;
                    }

                    layer.AcquireCell(
                        index,
                        build.temperature[index],
                        CalculateAccumulationMultiplier(data, index));
                }
            }
        }

        public void CompleteRestore(IRegionalWeatherService weather)
        {
            int sampledWeatherCount = 0;
            foreach (LayerState layer in _layers.Values)
            {
                foreach (CellState cell in layer.Cells.Values)
                {
                    if (!TileAllowsCoverage(layer, cell.LocalIndex))
                    {
                        if (!cell.Initialized)
                        {
                            cell.Amount = 0f;
                            cell.Initialized = true;
                        }
                        continue;
                    }

                    WeatherSample sample = GetCellWeather(
                        weather, cell, ref sampledWeatherCount);
                    if (TryGetWeatherOverrideCoverage(
                            layer.Data,
                            sample,
                            _indoorCells[cell.LocalIndex],
                            out float overrideCoverage))
                    {
                        cell.Amount = overrideCoverage;
                        cell.Initialized = true;
                        continue;
                    }

                    bool weatherAllowsAccumulation =
                        WeatherAllowsAccumulation(layer.Data, sample);

                    if (cell.Initialized)
                        continue;

                    if (TemperatureAllowsPersistence(layer.Data, sample))
                    {
                        cell.Amount = _chunk.TryGetNeighborCoverageSeed(
                            cell.LocalIndex,
                            layer.Data,
                            out float neighborAmount)
                            ? neighborAmount
                            : AllowsWeatherAccumulation(
                                _indoorCells[cell.LocalIndex],
                                weatherAllowsAccumulation)
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

            ClearWeatherSampleCache(sampledWeatherCount);

            _ready = true;
            RefreshAllVisuals(force: true);
        }

        public async Awaitable<bool> CompleteRestoreIncrementallyAsync(
            IRegionalWeatherService weather,
            Func<bool> isCurrent)
        {
            int sampledWeatherCount = 0;
            int processed = 0;
            try
            {
                foreach (LayerState layer in _layers.Values)
                {
                    foreach (CellState cell in layer.Cells.Values)
                    {
                        using (RestoreMarker.Auto())
                        {
                            if (!TileAllowsCoverage(layer, cell.LocalIndex))
                            {
                                if (!cell.Initialized)
                                {
                                    cell.Amount = 0f;
                                    cell.Initialized = true;
                                }
                            }
                            else
                            {
                                WeatherSample sample = GetCellWeather(
                                    weather, cell, ref sampledWeatherCount);
                                if (TryGetWeatherOverrideCoverage(
                                        layer.Data,
                                        sample,
                                        _indoorCells[cell.LocalIndex],
                                        out float overrideCoverage))
                                {
                                    cell.Amount = overrideCoverage;
                                    cell.Initialized = true;
                                }
                                else if (!cell.Initialized)
                                {
                                    bool weatherAllowsAccumulation =
                                        WeatherAllowsAccumulation(layer.Data, sample);
                                    cell.Amount = TemperatureAllowsPersistence(
                                            layer.Data, sample)
                                        ? _chunk.TryGetNeighborCoverageSeed(
                                            cell.LocalIndex,
                                            layer.Data,
                                            out float neighborAmount)
                                            ? neighborAmount
                                            : AllowsWeatherAccumulation(
                                                _indoorCells[cell.LocalIndex],
                                                weatherAllowsAccumulation)
                                                ? layer.Data.InitialCoverage
                                                : 0f
                                        : 0f;
                                    cell.Initialized = true;
                                }
                            }
                        }

                        if (++processed % 32 != 0)
                            continue;

                        await Awaitable.NextFrameAsync();
                        if (!isCurrent())
                            return false;
                    }
                }
            }
            finally
            {
                ClearWeatherSampleCache(sampledWeatherCount);
            }

            if (!isCurrent())
                return false;

            _ready = true;
            RefreshAllVisuals(force: true);
            return true;
        }

        public void Advance(
            IRegionalWeatherService weather,
            long elapsedTicks)
        {
            if (!_ready || elapsedTicks <= 0)
                return;

            float ticks = Mathf.Min(elapsedTicks, int.MaxValue);
            int dirtyCount = 0;
            int sampledWeatherCount = 0;

            using (SimulationMarker.Auto())
            {
                foreach (LayerState layer in _layers.Values)
                {
                    foreach (CellState cell in layer.Cells.Values)
                    {
                        if (!TileAllowsCoverage(layer, cell.LocalIndex))
                            continue;

                        WeatherSample sample = GetCellWeather(
                            weather, cell, ref sampledWeatherCount);
                        float previous = cell.Amount;
                        if (TryGetWeatherOverrideCoverage(
                                layer.Data,
                                sample,
                                _indoorCells[cell.LocalIndex],
                                out float overrideCoverage))
                        {
                            cell.Amount = overrideCoverage;
                        }
                        else
                        {
                            bool accumulationAllowed =
                                WeatherAllowsAccumulation(layer.Data, sample);
                            if (!TemperatureAllowsPersistence(layer.Data, sample))
                            {
                                cell.Amount = layer.Data.SlowlyDecayWhenTemperatureFails
                                    ? Mathf.Clamp01(
                                        cell.Amount - layer.Data.DecayRate * ticks)
                                    : 0f;
                            }
                            else
                            {
                                cell.Amount = ApplyWeatherAccumulation(
                                    cell.Amount,
                                    layer.Data.AccumulationRate *
                                    cell.AccumulationMultiplier * ticks,
                                    _indoorCells[cell.LocalIndex],
                                    accumulationAllowed);
                            }
                        }

                        if (previous != cell.Amount)
                        {
                            layer.VisibleCoverageDirty = true;
                            if (!_dirtyCellFlags[cell.LocalIndex])
                            {
                                _dirtyCellFlags[cell.LocalIndex] = true;
                                _dirtyCells[dirtyCount++] = cell.LocalIndex;
                            }
                        }
                    }
                }
            }
            ClearWeatherSampleCache(sampledWeatherCount);

            int visualCount = 0;
            bool navigationChanged = false;
            using (VisualMarker.Auto())
            {
                for (int i = 0; i < dirtyCount; i++)
                {
                    ushort index = _dirtyCells[i];
                    _dirtyCellFlags[index] = false;
                    CoverageData previousCoverage = _displayedData[index];
                    if (UpdateDisplayedVisual(index, force: false))
                    {
                        CoverageData currentCoverage = _displayedData[index];
                        navigationChanged |=
                            HasPathingHint(previousCoverage) ||
                            HasPathingHint(currentCoverage);
                        if (_coverageFadeRoutines[index] != null)
                            continue;

                        _visualChanges[visualCount++] = index;
                    }
                }
            }

            if (visualCount > 0)
                _chunk?.ApplyCoverageVisuals(
                    _visualChanges,
                    visualCount,
                    _displayedData,
                    _displayedAlpha);

            if (navigationChanged)
                _chunk?.NotifyNavigationChanged();

            if (dirtyCount > 0)
            {
                RefreshMaterialProperties();
            }
        }

        private WeatherSample SampleCellWeather(
            IRegionalWeatherService weather,
            CellState cell)
        {
            int size = ChunkBuildResult.ChunkSize;
            return weather.Sample(new Vector2(
                    _chunk.Position.x * size +
                    cell.LocalIndex % size +
                    0.5f,
                    _chunk.Position.y * size +
                    cell.LocalIndex / size +
                    0.5f),
                cell.TerrainTemperature);
        }

        private WeatherSample GetCellWeather(
            IRegionalWeatherService weather,
            CellState cell,
            ref int sampledCount)
        {
            ushort index = cell.LocalIndex;
            if (_weatherSampled[index])
                return _weatherSamples[index];

            WeatherSample sample = SampleCellWeather(weather, cell);
            _weatherSamples[index] = sample;
            _weatherSampled[index] = true;
            _sampledWeatherCells[sampledCount++] = index;
            return sample;
        }

        private void ClearWeatherSampleCache(int sampledCount)
        {
            for (int i = 0; i < sampledCount; i++)
                _weatherSampled[_sampledWeatherCells[i]] = false;
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
                !_layers.TryGetValue(
                    coverage.CoverageId,
                    out LayerState layer) ||
                !TileAllowsCoverage(layer, localIndex) ||
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
                !TileAllowsCoverage(layer, localIndex) ||
                !layer.Cells.TryGetValue(
                    localIndex,
                    out CellState cell))
            {
                return false;
            }

            cell.Amount = Mathf.Clamp01(amount);
            cell.Initialized = true;
            layer.VisibleCoverageDirty = true;
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

        public bool TryGetPathingCoverage(
            ushort localIndex,
            out CoverageData data,
            out float amount)
        {
            bool found = TryGetDisplayedCoverage(
                localIndex,
                out LayerState layer,
                out CellState cell);
            data = found ? layer.Data : null;
            amount = found ? cell.Amount : 0f;
            return found && data.PathingTerrain !=
                TileData.AiPathingTerrain.Normal;
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

            float startAmount = _coverageFadeRoutines[localIndex] != null
                ? _animatedCoverageAmounts[localIndex]
                : cell.Amount;
            CoverageData fadingCoverage = layer.Data;

            cell.Amount = Mathf.Max(0f, cell.Amount - amount);
            cell.Initialized = true;
            layer.VisibleCoverageDirty = true;

            UpdateDisplayedVisual(localIndex, force: true);
            StartCoverageFade(
                localIndex,
                fadingCoverage,
                startAmount);
            RefreshMaterialProperties();
            return true;
        }

        public void RefreshCell(ushort localIndex)
        {
            foreach (LayerState layer in _layers.Values)
            {
                layer.InvalidateTileEligibility(localIndex);
                layer.VisibleCoverageDirty = true;
            }
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
                layer.VisibleCoverageDirty = true;
                found = true;
            }

            if (found && _ready)
                RefreshCell(localIndex);
        }

        /// <summary>
        /// Clears coverage in the portion of a world-space tile area owned by
        /// this chunk. This is applied after restore for deterministic world
        /// features such as the spawn platform.
        /// </summary>
        public void ClearWorldArea(RectInt worldArea)
        {
            if (_chunk == null ||
                worldArea.width <= 0 ||
                worldArea.height <= 0)
            {
                return;
            }

            int size = ChunkBuildResult.ChunkSize;
            RectInt chunkArea = new(
                _chunk.Position.x * size,
                _chunk.Position.y * size,
                size,
                size);
            int minimumX = Mathf.Max(worldArea.xMin, chunkArea.xMin);
            int maximumX = Mathf.Min(worldArea.xMax, chunkArea.xMax);
            int minimumY = Mathf.Max(worldArea.yMin, chunkArea.yMin);
            int maximumY = Mathf.Min(worldArea.yMax, chunkArea.yMax);
            if (minimumX >= maximumX || minimumY >= maximumY)
                return;

            int visualCount = 0;
            bool amountChanged = false;
            for (int worldY = minimumY; worldY < maximumY; worldY++)
            {
                for (int worldX = minimumX; worldX < maximumX; worldX++)
                {
                    int localX = worldX - chunkArea.xMin;
                    int localY = worldY - chunkArea.yMin;
                    ushort localIndex =
                        checked((ushort)(localX + localY * size));

                    foreach (LayerState layer in _layers.Values)
                    {
                        if (!layer.Cells.TryGetValue(
                                localIndex,
                                out CellState cell))
                        {
                            continue;
                        }

                        amountChanged |= cell.Amount > 0f;
                        cell.Amount = 0f;
                        cell.Initialized = true;
                        layer.VisibleCoverageDirty = true;
                    }

                    if (_ready &&
                        UpdateDisplayedVisual(localIndex, force: false))
                    {
                        CancelCoverageFade(localIndex);
                        _visualChanges[visualCount++] = localIndex;
                    }
                }
            }

            if (visualCount > 0)
            {
                _chunk.ApplyCoverageVisuals(
                    _visualChanges,
                    visualCount,
                    _displayedData,
                    _displayedAlpha);
            }

            if (amountChanged)
                RefreshMaterialProperties();
        }

        public void SetRoomInteriorCells(
            IReadOnlyList<ushort> localIndices,
            bool isInterior)
        {
            if (localIndices == null || localIndices.Count == 0)
                return;

            for (int i = 0; i < localIndices.Count; i++)
            {
                ushort localIndex = localIndices[i];
                if (localIndex >= MaximumSavedCells)
                    continue;

                _indoorCells[localIndex] = isInterior;
            }
        }

        public static bool AllowsWeatherAccumulation(
            bool isRoomInterior,
            bool weatherAllowsAccumulation) =>
            !isRoomInterior && weatherAllowsAccumulation;

        public static float ApplyWeatherAccumulation(
            float currentAmount,
            float accumulatedAmount,
            bool isRoomInterior,
            bool weatherAllowsAccumulation)
        {
            currentAmount = Mathf.Clamp01(currentAmount);
            if (!AllowsWeatherAccumulation(
                    isRoomInterior,
                    weatherAllowsAccumulation))
            {
                return currentAmount;
            }

            return Mathf.Clamp01(
                currentAmount + Mathf.Max(0f, accumulatedAmount));
        }

        public void RefreshCellColor(ushort localIndex) =>
            RefreshVisual(localIndex, force: true);

        private bool TileAllowsCoverage(
            LayerState layer,
            ushort localIndex)
        {
            if (localIndex >= MaximumSavedCells)
                return false;

            if (layer.TryGetTileEligibility(localIndex, out bool eligible))
                return eligible;

            eligible = _chunk != null &&
                       _chunk.TryGetCoverageGroundTile(
                           localIndex,
                           out TileData tile) &&
                       layer.Data.AllowsTile(tile);
            layer.SetTileEligibility(localIndex, eligible);
            return eligible;
        }

        public void PrepareForPool()
        {
            CancelAllCoverageFades();
            unchecked
            {
                _generation++;
            }
            _ready = false;
            _displayedMaterialData = null;
            RecycleLayers();
            Array.Clear(_indoorCells, 0, _indoorCells.Length);
            _chunk?.ClearCoverageVisuals();
            _chunk = null;
        }

        private void RecycleLayers()
        {
            foreach (LayerState layer in _layers.Values)
            {
                layer.PrepareForPool();
                _layerPool.Push(layer);
            }

            _layers.Clear();
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
                sample.HasWeatherOverride &&
                sample.WeatherOverrideInfluence <= 0f ||
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

        public static bool TryGetWeatherOverrideCoverage(
            CoverageData data,
            WeatherSample sample,
            bool isIndoor,
            out float coverage)
        {
            coverage = 0f;
            if (data == null ||
                !sample.HasWeatherOverride ||
                !ContainsExplicitId(
                    data.AllowedWeatherIds,
                    sample.WeatherId))
            {
                return false;
            }

            coverage = isIndoor
                ? 0f
                : sample.WeatherOverrideInfluence;
            return true;
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

        private static bool ContainsExplicitId(
            string[] allowedIds,
            string currentId)
        {
            if (allowedIds == null ||
                string.IsNullOrWhiteSpace(currentId))
            {
                return false;
            }

            foreach (string allowed in allowedIds)
            {
                if (string.Equals(
                        allowed,
                        currentId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasPathingHint(CoverageData data)
        {
            return data != null &&
                   data.PathingTerrain !=
                   TileData.AiPathingTerrain.Normal;
        }

        private void RefreshAllVisuals(bool force)
        {
            CancelAllCoverageFades();
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
            CancelCoverageFade(localIndex);
            if (!UpdateDisplayedVisual(localIndex, force))
                return;

            _visualChanges[0] = localIndex;
            _chunk?.ApplyCoverageVisuals(
                _visualChanges,
                1,
                _displayedData,
                _displayedAlpha);
        }

        private void StartCoverageFade(
            ushort localIndex,
            CoverageData coverage,
            float startAmount)
        {
            CancelCoverageFade(localIndex);

            if (coverageMiningFadeDuration <= 0f ||
                !isActiveAndEnabled ||
                !Application.isPlaying)
            {
                RefreshVisual(localIndex, force: true);
                return;
            }

            _animatedCoverageAmounts[localIndex] =
                Mathf.Clamp01(startAmount);
            _coverageFadeRoutines[localIndex] = StartCoroutine(
                FadeCoverageVisual(
                    localIndex,
                    coverage,
                    Mathf.Clamp01(startAmount)));
        }

        private IEnumerator FadeCoverageVisual(
            ushort localIndex,
            CoverageData coverage,
            float startAmount)
        {
            float elapsed = 0f;
            while (elapsed < coverageMiningFadeDuration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(
                    elapsed / coverageMiningFadeDuration);
                float targetAmount = GetPersistedDisplayedAmount(
                    localIndex,
                    coverage);
                float amount = Mathf.Lerp(
                    startAmount,
                    targetAmount,
                    Mathf.SmoothStep(0f, 1f, progress));
                _animatedCoverageAmounts[localIndex] = amount;
                _chunk?.ApplyCoverageVisual(
                    localIndex,
                    coverage,
                    amount);
                yield return null;
            }

            _coverageFadeRoutines[localIndex] = null;
            RefreshVisual(localIndex, force: true);
        }

        private float GetPersistedDisplayedAmount(
            ushort localIndex,
            CoverageData expectedCoverage)
        {
            return TryGetDisplayedCoverage(
                       localIndex,
                       out LayerState persistedLayer,
                       out CellState persistedCell) &&
                   persistedLayer.Data == expectedCoverage
                ? persistedCell.Amount
                : 0f;
        }

        private void CancelCoverageFade(ushort localIndex)
        {
            Coroutine routine = _coverageFadeRoutines[localIndex];
            if (routine == null)
                return;

            StopCoroutine(routine);
            _coverageFadeRoutines[localIndex] = null;
        }

        private void CancelAllCoverageFades()
        {
            for (ushort index = 0;
                 index < MaximumSavedCells;
                 index++)
            {
                CancelCoverageFade(index);
            }
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
                    !TileAllowsCoverage(layer, localIndex))
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
                if (!LayerHasVisibleCoverage(layer))
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

        private bool LayerHasVisibleCoverage(LayerState layer)
        {
            if (!layer.VisibleCoverageDirty)
                return layer.HasVisibleCoverage;

            layer.HasVisibleCoverage = false;
            foreach (CellState cell in layer.Cells.Values)
            {
                if (cell.Amount <= 0f ||
                    !TileAllowsCoverage(layer, cell.LocalIndex))
                {
                    continue;
                }

                layer.HasVisibleCoverage = true;
                break;
            }

            layer.VisibleCoverageDirty = false;
            return layer.HasVisibleCoverage;
        }
    }
}
