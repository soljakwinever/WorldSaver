using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    /// <summary>
    /// Captures Unity tile state incrementally, evaluates spreading on a worker
    /// thread, then applies a bounded number of persistent changes per frame.
    /// </summary>
    public sealed class TileSpreadSystem : MonoBehaviour
    {
        [SerializeField, Min(32)] private int cellsCapturedPerFrame = 512;
        [SerializeField, Min(1)] private int terrainSamplesPerFrame = 64;
        [SerializeField, Min(1)] private int changesAppliedPerFrame = 32;

        private readonly List<Chunk> _loadedChunks = new();
        private readonly Dictionary<TileSpreadRule, long> _nextRuleTicks = new();
        private readonly Queue<SpreadResult> _results = new();

        private WorldData _worldData;
        private Chunkloader _chunkloader;
        private IWorldClock _worldClock;
        private WorldGeneration _worldGeneration;
        private SpreadWork _work;
        private CancellationTokenSource _cancellation;

        [Inject]
        public void Construct(
            WorldData worldData,
            Chunkloader chunkloader,
            IWorldClock worldClock,
            WorldGeneration worldGeneration)
        {
            _worldData = worldData ?? throw new ArgumentNullException(nameof(worldData));
            _chunkloader = chunkloader ?? throw new ArgumentNullException(nameof(chunkloader));
            _worldClock = worldClock ?? throw new ArgumentNullException(nameof(worldClock));
            _worldGeneration = worldGeneration ??
                throw new ArgumentNullException(nameof(worldGeneration));
        }

        private void Awake()
        {
            _cancellation = new CancellationTokenSource();
        }

        private void Update()
        {
            ApplyResults();
            AdvanceWork();

            if (_work == null)
                TryBeginDueRule();
        }

        private void TryBeginDueRule()
        {
            if (_worldData == null || _worldClock == null)
                return;

            TileSpreadRule[] rules = _worldData.tileSpreadRules;
            if (rules == null)
                return;

            long currentTick = _worldClock.CurrentTick;
            for (int i = 0; i < rules.Length; i++)
            {
                TileSpreadRule rule = rules[i];
                if (!ShouldEvaluate(rule, currentTick))
                    continue;

                _chunkloader.CopyLoadedChunks(_loadedChunks);
                _work = new SpreadWork(CreateRuleSnapshot(rule), _loadedChunks);
                _nextRuleTicks[rule] = SaturatingAdd(currentTick, rule.intervalTicks);
                return;
            }
        }

        private void AdvanceWork()
        {
            if (_work == null)
                return;

            switch (_work.Phase)
            {
                case WorkPhase.CapturingTiles:
                    CaptureTiles();
                    break;
                case WorkPhase.FindingCandidates:
                    ReceiveCandidates();
                    break;
                case WorkPhase.CapturingTerrain:
                    CaptureTerrain();
                    break;
                case WorkPhase.FilteringCandidates:
                    ReceiveResults();
                    break;
            }
        }

        private void CaptureTiles()
        {
            int remaining = cellsCapturedPerFrame;
            while (remaining-- > 0 && _work.ChunkIndex < _work.Chunks.Count)
            {
                Chunk chunk = _work.Chunks[_work.ChunkIndex];
                int localIndex = _work.LocalCellIndex++;
                int x = localIndex % ChunkBuildResult.ChunkSize;
                int y = localIndex / ChunkBuildResult.ChunkSize;
                Vector2Int origin = chunk.Position * ChunkBuildResult.ChunkSize;
                Vector3Int worldCell = new(origin.x + x, origin.y + y, 0);

                if (chunk.TryGetTileData(
                        worldCell,
                        _work.Rule.Layer,
                        out TileData tile))
                {
                    CellKey key = new(worldCell.x, worldCell.y);
                    _work.Cells[key] = new CellState(
                        tile.TileId,
                        chunk.IsTileChanged(worldCell, _work.Rule.Layer));
                }

                if (_work.LocalCellIndex >=
                    ChunkBuildResult.ChunkSize * ChunkBuildResult.ChunkSize)
                {
                    _work.LocalCellIndex = 0;
                    _work.ChunkIndex++;
                }
            }

            if (_work.ChunkIndex < _work.Chunks.Count)
                return;

            RuleSnapshot rule = _work.Rule;
            Dictionary<CellKey, CellState> cells = _work.Cells;
            CancellationToken token = _cancellation.Token;
            _work.CandidateTask = Task.Run(
                () => FindCandidates(rule, cells, token),
                token);
            _work.Phase = WorkPhase.FindingCandidates;
        }

        private void ReceiveCandidates()
        {
            if (!_work.CandidateTask.IsCompleted)
                return;
            if (_work.CandidateTask.IsCanceled || _work.CandidateTask.IsFaulted)
            {
                LogTaskFailure(_work.CandidateTask.Exception);
                _work = null;
                return;
            }

            _work.Candidates = _work.CandidateTask.Result;
            _work.Phase = WorkPhase.CapturingTerrain;
        }

        private void CaptureTerrain()
        {
            int remaining = terrainSamplesPerFrame;
            while (remaining-- > 0 &&
                   _work.TerrainIndex < _work.Candidates.Count)
            {
                CellKey cell = _work.Candidates[_work.TerrainIndex++];
                TerrainSample sample =
                    _worldGeneration.GetTerrainSample(cell.X, cell.Y);
                _work.Terrain[cell] =
                    TerrainState.From(sample, _work.AllowedBiomes);
            }

            if (_work.TerrainIndex < _work.Candidates.Count)
                return;

            RuleSnapshot rule = _work.Rule;
            List<CellKey> candidates = _work.Candidates;
            Dictionary<CellKey, TerrainState> terrain = _work.Terrain;
            CancellationToken token = _cancellation.Token;
            int seed = unchecked((int)_worldClock.CurrentTick * 397 ^ rule.RuleId);
            _work.ResultTask = Task.Run(
                () => FilterCandidates(rule, candidates, terrain, seed, token),
                token);
            _work.Phase = WorkPhase.FilteringCandidates;
        }

        private void ReceiveResults()
        {
            if (!_work.ResultTask.IsCompleted)
                return;
            if (_work.ResultTask.IsCanceled || _work.ResultTask.IsFaulted)
            {
                LogTaskFailure(_work.ResultTask.Exception);
                _work = null;
                return;
            }

            foreach (CellKey cell in _work.ResultTask.Result)
            {
                TerrainSample sample =
                    _worldGeneration.GetTerrainSample(cell.X, cell.Y);
                _results.Enqueue(new SpreadResult(
                    cell,
                    _work.Rule.Layer,
                    _work.ResultTile,
                    _work.Cells[cell].TileId,
                    _work.Rule.TargetOrigins,
                    sample.biomeBlend.groundColor));
            }

            _work = null;
        }

        private void ApplyResults()
        {
            int remaining = changesAppliedPerFrame;
            while (remaining-- > 0 && _results.Count > 0)
            {
                SpreadResult result = _results.Dequeue();
                Vector3Int cell = new(result.Cell.X, result.Cell.Y, 0);
                if (!_chunkloader.TryGetLoadedChunk(cell, out Chunk chunk) ||
                    !chunk.TryGetTileData(cell, result.Layer, out TileData current) ||
                    current.TileId != result.ExpectedTileId ||
                    !AllowsOrigin(
                        chunk.IsTileChanged(cell, result.Layer),
                        result.TargetOrigins))
                {
                    continue;
                }

                chunk.TryPlaceTile(
                    cell,
                    result.Layer,
                    result.Tile,
                    result.Color,
                    PersistentTileTint.BiomeGround);
            }
        }

        private bool ShouldEvaluate(TileSpreadRule rule, long currentTick)
        {
            if (rule == null || rule.sourceTile == null || rule.Result == null ||
                rule.validTargets == null || rule.validTargets.Length == 0 ||
                rule.chancePerTarget <= 0f)
                return false;

            if (_nextRuleTicks.TryGetValue(rule, out long nextTick))
                return currentTick >= nextTick;

            _nextRuleTicks.Add(
                rule,
                SaturatingAdd(currentTick, Math.Max(1, rule.intervalTicks)));
            return false;
        }

        private static List<CellKey> FindCandidates(
            RuleSnapshot rule,
            Dictionary<CellKey, CellState> cells,
            CancellationToken token)
        {
            HashSet<CellKey> candidates = new();
            foreach (KeyValuePair<CellKey, CellState> entry in cells)
            {
                token.ThrowIfCancellationRequested();
                if (entry.Value.TileId != rule.SourceTileId ||
                    !AllowsOrigin(entry.Value.Changed, rule.SourceOrigins))
                    continue;

                AddNeighbours(entry.Key, rule, cells, candidates);
            }

            return new List<CellKey>(candidates);
        }

        private static void AddNeighbours(
            CellKey source,
            RuleSnapshot rule,
            Dictionary<CellKey, CellState> cells,
            HashSet<CellKey> candidates)
        {
            int count = rule.IncludeDiagonals ? 8 : 4;
            for (int i = 0; i < count; i++)
            {
                CellKey target = source.Offset(Offsets[i, 0], Offsets[i, 1]);
                if (cells.TryGetValue(target, out CellState state) &&
                    rule.TargetTileIds.Contains(state.TileId) &&
                    AllowsOrigin(state.Changed, rule.TargetOrigins))
                    candidates.Add(target);
            }
        }

        private static List<CellKey> FilterCandidates(
            RuleSnapshot rule,
            List<CellKey> candidates,
            Dictionary<CellKey, TerrainState> terrain,
            int seed,
            CancellationToken token)
        {
            System.Random random = new(seed);
            List<CellKey> results = new();
            for (int i = 0; i < candidates.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                CellKey cell = candidates[i];
                if (terrain.TryGetValue(cell, out TerrainState state) &&
                    MatchesTerrain(rule, state) &&
                    random.NextDouble() <= rule.Chance)
                    results.Add(cell);
            }
            return results;
        }

        private static bool MatchesTerrain(RuleSnapshot rule, TerrainState sample)
        {
            if (!rule.RestrictTerrain)
                return true;
            return sample.Height >= rule.MinHeight && sample.Height <= rule.MaxHeight &&
                   sample.Moisture >= rule.MinMoisture && sample.Moisture <= rule.MaxMoisture &&
                   sample.Temperature >= rule.MinTemperature &&
                   sample.Temperature <= rule.MaxTemperature &&
                   MatchesFeature(sample.Water, rule.Water) &&
                   MatchesFeature(sample.Cliff, rule.Cliff) &&
                   MatchesFeature(sample.Road, rule.Road) &&
                   MatchesFeature(sample.Trail, rule.Trail) &&
                   (!rule.RestrictsBiomes || sample.BiomeAllowed);
        }

        private static bool MatchesFeature(bool value, TerrainFeatureRequirement requirement)
        {
            return requirement == TerrainFeatureRequirement.Ignore ||
                   (requirement == TerrainFeatureRequirement.Required && value) ||
                   (requirement == TerrainFeatureRequirement.Forbidden && !value);
        }

        private static bool AllowsOrigin(bool changed, TileOriginFilter filter)
        {
            return (filter & (changed
                ? TileOriginFilter.Changed
                : TileOriginFilter.Generated)) != 0;
        }

        private static RuleSnapshot CreateRuleSnapshot(TileSpreadRule rule)
        {
            HashSet<int> targetIds = new();
            foreach (TileData tile in rule.validTargets)
                if (tile != null)
                    targetIds.Add(tile.TileId);

            return new RuleSnapshot(rule, targetIds);
        }

        private void OnDestroy()
        {
            _cancellation?.Cancel();
            _cancellation?.Dispose();
        }

        private void OnValidate()
        {
            cellsCapturedPerFrame = Mathf.Max(32, cellsCapturedPerFrame);
            terrainSamplesPerFrame = Mathf.Max(1, terrainSamplesPerFrame);
            changesAppliedPerFrame = Mathf.Max(1, changesAppliedPerFrame);
        }

        private static void LogTaskFailure(AggregateException exception)
        {
            if (exception != null)
                Debug.LogException(exception.Flatten());
        }

        private static long SaturatingAdd(long value, long amount)
        {
            amount = Math.Max(1, amount);
            return value > long.MaxValue - amount ? long.MaxValue : value + amount;
        }

        private static readonly int[,] Offsets =
        {
            { 0, 1 }, { 1, 0 }, { 0, -1 }, { -1, 0 },
            { 1, 1 }, { 1, -1 }, { -1, -1 }, { -1, 1 }
        };

        private enum WorkPhase
        {
            CapturingTiles,
            FindingCandidates,
            CapturingTerrain,
            FilteringCandidates
        }

        private sealed class SpreadWork
        {
            public readonly RuleSnapshot Rule;
            public readonly TileData ResultTile;
            public readonly BiomeData[] AllowedBiomes;
            public readonly List<Chunk> Chunks;
            public readonly Dictionary<CellKey, CellState> Cells = new();
            public readonly Dictionary<CellKey, TerrainState> Terrain = new();
            public WorkPhase Phase;
            public int ChunkIndex;
            public int LocalCellIndex;
            public int TerrainIndex;
            public Task<List<CellKey>> CandidateTask;
            public Task<List<CellKey>> ResultTask;
            public List<CellKey> Candidates;

            public SpreadWork(RuleSnapshot rule, List<Chunk> chunks)
            {
                Rule = rule;
                ResultTile = rule.ResultTile;
                AllowedBiomes = rule.AllowedBiomes;
                Chunks = new List<Chunk>(chunks);
            }
        }

        private sealed class RuleSnapshot
        {
            public readonly int RuleId;
            public readonly int SourceTileId;
            public readonly TileData ResultTile;
            public readonly PersistentTileLayer Layer;
            public readonly TileOriginFilter SourceOrigins;
            public readonly TileOriginFilter TargetOrigins;
            public readonly HashSet<int> TargetTileIds;
            public readonly bool IncludeDiagonals;
            public readonly bool RestrictTerrain;
            public readonly bool RestrictsBiomes;
            public readonly BiomeData[] AllowedBiomes;
            public readonly float MinHeight, MaxHeight, MinMoisture, MaxMoisture;
            public readonly float MinTemperature, MaxTemperature, Chance;
            public readonly TerrainFeatureRequirement Water, Cliff, Road, Trail;

            public RuleSnapshot(
                TileSpreadRule rule,
                HashSet<int> targetIds)
            {
                RuleId = unchecked(
                    rule.sourceTile.TileId * 397 ^
                    rule.Result.TileId * 17 ^
                    (int)rule.layer);
                SourceTileId = rule.sourceTile.TileId;
                ResultTile = rule.Result;
                Layer = rule.layer;
                SourceOrigins = rule.allowedSourceOrigins;
                TargetOrigins = rule.allowedTargetOrigins;
                TargetTileIds = targetIds;
                IncludeDiagonals = rule.includeDiagonals;
                RestrictTerrain = rule.restrictByTerrain;
                AllowedBiomes = rule.allowedBiomes == null
                    ? Array.Empty<BiomeData>()
                    : (BiomeData[])rule.allowedBiomes.Clone();
                RestrictsBiomes = AllowedBiomes.Length > 0;
                MinHeight = rule.minHeight;
                MaxHeight = rule.maxHeight;
                MinMoisture = rule.minMoisture;
                MaxMoisture = rule.maxMoisture;
                MinTemperature = rule.minTemperature;
                MaxTemperature = rule.maxTemperature;
                Chance = rule.chancePerTarget;
                Water = rule.water;
                Cliff = rule.cliff;
                Road = rule.road;
                Trail = rule.trail;
            }
        }

        private readonly struct CellKey : IEquatable<CellKey>
        {
            public readonly int X;
            public readonly int Y;
            public CellKey(int x, int y) { X = x; Y = y; }
            public CellKey Offset(int x, int y) => new(X + x, Y + y);
            public bool Equals(CellKey other) => X == other.X && Y == other.Y;
            public override bool Equals(object obj) => obj is CellKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(X, Y);
        }

        private readonly struct CellState
        {
            public readonly int TileId;
            public readonly bool Changed;
            public CellState(int tileId, bool changed) { TileId = tileId; Changed = changed; }
        }

        private readonly struct TerrainState
        {
            public readonly float Height, Moisture, Temperature;
            public readonly bool BiomeAllowed;
            public readonly bool Water, Cliff, Road, Trail;

            private TerrainState(
                TerrainSample sample,
                BiomeData[] allowedBiomes)
            {
                Height = sample.height;
                Moisture = sample.moisture;
                Temperature = sample.temperature;
                BiomeAllowed = IsBiomeAllowed(sample.biome, allowedBiomes);
                Water = sample.isWater;
                Cliff = sample.isCliff;
                Road = sample.isRoad;
                Trail = sample.isTrail;
            }

            public static TerrainState From(
                TerrainSample sample,
                BiomeData[] allowedBiomes)
            {
                return new TerrainState(sample, allowedBiomes);
            }

            private static bool IsBiomeAllowed(
                BiomeData biome,
                BiomeData[] allowedBiomes)
            {
                if (allowedBiomes == null || allowedBiomes.Length == 0)
                    return true;
                for (int i = 0; i < allowedBiomes.Length; i++)
                    if (allowedBiomes[i] == biome)
                        return true;
                return false;
            }
        }

        private readonly struct SpreadResult
        {
            public readonly CellKey Cell;
            public readonly PersistentTileLayer Layer;
            public readonly TileData Tile;
            public readonly int ExpectedTileId;
            public readonly TileOriginFilter TargetOrigins;
            public readonly Color Color;
            public SpreadResult(
                CellKey cell,
                PersistentTileLayer layer,
                TileData tile,
                int expectedTileId,
                TileOriginFilter targetOrigins,
                Color color)
            {
                Cell = cell;
                Layer = layer;
                Tile = tile;
                ExpectedTileId = expectedTileId;
                TargetOrigins = targetOrigins;
                Color = color;
            }
        }
    }
}
