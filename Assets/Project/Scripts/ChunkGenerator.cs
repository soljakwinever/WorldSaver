using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Project.Scripts.Bus;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    public class ChunkGenerator : IChunkGenerator
    {
        public bool IsRunning { get; private set; }
        
        private readonly Queue<Vector2Int> pendingChunks = new Queue<Vector2Int>();
        private readonly List<RunningChunkTask> runningTasks = new();
        private readonly Queue<ChunkBuildResult> completedChunks = new ();

        private sealed class RunningChunkTask
        {
            public Vector2Int Position;
            public Task<ChunkBuildResult> Task;
            public CancellationTokenSource Cancellation;
        }
        
        public int maxConcurrentTasks = 2;
        public int maxChunksAppliedPerFrame = 1;

        private readonly HashSet<Vector2Int> queuedOrRunning = new();
        private readonly HashSet<Vector2Int> completed = new();
        
        [Inject] private WorldGeneration worldGeneration;
        [Inject] private Chunk.Pool chunkPool;
        
        [Inject] private MapSignalBus mapSignalBus;
        
        private IChunkLoader _chunkLoader;

        public void RequestChunk(Vector2Int position)
        {
            if(completed.Contains(position))
                return;
            
            if(queuedOrRunning.Contains(position))
                return;
            
            pendingChunks.Enqueue(position);
            queuedOrRunning.Add(position);
        }

        public async Awaitable Run(IChunkLoader chunkloader, CancellationToken cancellationToken)
        {
            _chunkLoader = chunkloader;
            IsRunning = true;
            do
            {
                CancelDistantTasks();
                StartPendingTasks(cancellationToken);
                CollectCompletedTasks();

                int applied = 0;

                while (completedChunks.Count > 0 && applied < maxChunksAppliedPerFrame && !cancellationToken.IsCancellationRequested)
                {
                    var result = completedChunks.Dequeue();
                    if(cancellationToken.IsCancellationRequested) break;

                    if (!_chunkLoader.ShouldLoadChunk(result.chunkPosition))
                    {
                        queuedOrRunning.Remove(result.chunkPosition);
                        continue;
                    }

                    applied++;
                    
                    mapSignalBus.RaiseChunkBuilt(result);
                    //chunkloader.ReportSpawn(chunk);
                    
                    completed.Add(result.chunkPosition);
                    queuedOrRunning.Remove(result.chunkPosition);
                }

                await Awaitable.NextFrameAsync(cancellationToken);
            } while (!cancellationToken.IsCancellationRequested);

            void CollectCompletedTasks()
            {
                for (int i = runningTasks.Count - 1; i >= 0; i--)
                {
                    RunningChunkTask running = runningTasks[i];
                    Task<ChunkBuildResult> task = running.Task;
                    
                    if(!task.IsCompleted)
                        continue;
                    
                    runningTasks.RemoveAt(i);

                    running.Cancellation.Dispose();

                    if (task.IsFaulted)
                    {
                        Debug.LogException(task.Exception);
                        queuedOrRunning.Remove(running.Position);
                        continue;
                    }
                    if (task.IsCanceled ||
                        !_chunkLoader.ShouldLoadChunk(running.Position))
                    {
                        queuedOrRunning.Remove(running.Position);
                        continue;
                    }
                    
                    completedChunks.Enqueue(task.Result);
                }
            }

            void CancelDistantTasks()
            {
                foreach (RunningChunkTask running in runningTasks)
                {
                    if (!_chunkLoader.ShouldLoadChunk(running.Position))
                        running.Cancellation.Cancel();
                }
            }
        }

        public void ChunkUnloaded(Vector2Int position)
        {
            completed.Remove(position);
            queuedOrRunning.Remove(position);
        }

        private void StartPendingTasks(CancellationToken cancellationToken = default)
        {
            // Fill every available worker slot immediately. Starting at most one
            // task per frame left cores idle between completions and made the
            // initial ring of chunks take noticeably longer to become ready.
            while (pendingChunks.Count > 0 &&
                   runningTasks.Count < Mathf.Max(1, maxConcurrentTasks))
            {
                var chunkPosition = pendingChunks.Dequeue();
                if (_chunkLoader == null ||
                    !_chunkLoader.ShouldLoadChunk(chunkPosition))
                {
                    queuedOrRunning.Remove(chunkPosition);
                    continue;
                }
                
                //Todo: World Snapshot

                var propSpawnRules = worldGeneration.PropSpawnRules
                    .ShuffleXY(chunkPosition.x, chunkPosition.y)
                    .ToArray();
                
                CancellationTokenSource chunkCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);
                Task<ChunkBuildResult> task = Task.Run(
                    () => BuildChunk(
                        chunkPosition,
                        worldGeneration,
                        propSpawnRules,
                        chunkCancellation.Token),
                    chunkCancellation.Token);

                runningTasks.Add(new RunningChunkTask
                {
                    Position = chunkPosition,
                    Task = task,
                    Cancellation = chunkCancellation
                });
            }
        }

        private static ChunkBuildResult BuildChunk(Vector2Int position, WorldGeneration worldGeneration, IEnumerable<PropSpawnRule> propSpawnRules, CancellationToken cancellationToken = default)
        {
            // Guaranteed features and their spawn-to-village road depend on
            // the canonical spawn anchor. Resolve it before the first terrain
            // cell so the initial chunk ring cannot be permanently generated
            // without those overlays because of Unity Start-order timing.
            _ = worldGeneration.WorldSpawnPosition;

            ChunkBuildResult result = new ChunkBuildResult(position);
            bool[] restrictedPathCells =
                new bool[ChunkBuildResult.ChunkSize * ChunkBuildResult.ChunkSize];
            worldGeneration.BuildRoadMasks(
                position,
                result.isRoad,
                restrictedPathCells);

            int offsetX = position.x * ChunkBuildResult.ChunkSize;
            int offsetY = position.y * ChunkBuildResult.ChunkSize;

            for (int y = 0; y < ChunkBuildResult.ChunkSize; y++)
            {
                for (int x = 0; x < ChunkBuildResult.ChunkSize; x++)
                {
                    var worldX = offsetX + x;
                    var worldY = offsetY + y;
                    
                    int index = result.GetTileIndex(x, y);

                    int tileIndex = worldGeneration.GetTileForChunk(
                        worldX,
                        worldY,
                        out result.biomeData[index],
                        out result.heights[index],
                        out result.moisture[index],
                        out result.temperature[index],
                        out result.floorTiles[index],
                        out result.terrainKinds[index],
                        out result.isCliff[index]);
                    
                    result.tileIndexes[index] = tileIndex;
                    if (result.isRoad[index] &&
                        result.floorTiles[index] == null &&
                        result.terrainKinds[index] == TerrainKind.Floor &&
                        result.heights[index] > worldGeneration.Elevation.waterHeight)
                    {
                        result.floorTiles[index] =
                            worldGeneration.Preset.features.roadTile ??
                            result.biomeData[index].dominantBiome?.overridePathTile;
                    }
                    
                    if(cancellationToken.IsCancellationRequested)
                        break;
                }
                if(cancellationToken.IsCancellationRequested)
                    break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!worldGeneration.UsesCaveLayout)
            {
                // IsSmallCliff samples the center and five neighbors. Calling it
                // independently for every cell repeated almost all terrain work
                // six times. Cache this chunk plus the narrow halo needed by the
                // classifier, then reuse those heights for every cell.
                const int horizontalPadding = 1;
                const int bottomPadding = 1;
                const int topPadding = 2;
                int sampleWidth = IChunk.ChunkSize + horizontalPadding * 2;
                int sampleHeight = IChunk.ChunkSize + bottomPadding + topPadding;
                float[] cliffHeights = new float[sampleWidth * sampleHeight];

                for (int sampleY = -bottomPadding;
                     sampleY < IChunk.ChunkSize + topPadding;
                     sampleY++)
                {
                    for (int sampleX = -horizontalPadding;
                         sampleX < IChunk.ChunkSize + horizontalPadding;
                         sampleX++)
                    {
                        int sampleIndex =
                            sampleX + horizontalPadding +
                            (sampleY + bottomPadding) * sampleWidth;
                        bool isInside =
                            (uint)sampleX < IChunk.ChunkSize &&
                            (uint)sampleY < IChunk.ChunkSize;
                        if (isInside)
                        {
                            cliffHeights[sampleIndex] =
                                result.heights[result.GetTileIndex(sampleX, sampleY)];
                        }
                        else
                        {
                            worldGeneration.GetTile(
                                offsetX + sampleX,
                                offsetY + sampleY,
                                out _,
                                out cliffHeights[sampleIndex],
                                out _,
                                out _,
                                out _);
                        }

                        if (cancellationToken.IsCancellationRequested)
                            break;
                    }
                    if (cancellationToken.IsCancellationRequested)
                        break;
                }

                for (int y = 0; y < IChunk.ChunkSize; y++)
                for (int x = 0; x < IChunk.ChunkSize; x++)
                {
                    int center = x + horizontalPadding +
                                 (y + bottomPadding) * sampleWidth;
                    float height = cliffHeights[center];
                    float right = cliffHeights[center + 1];
                    float left = cliffHeights[center - 1];
                    float up = cliffHeights[center + sampleWidth];
                    float down = cliffHeights[center - sampleWidth];
                    float upTwo = cliffHeights[center + sampleWidth * 2];
                    float highestNeighbor =
                        Mathf.Max(right, left, up, Mathf.Max(down, upTwo));
                    float upwardJump = highestNeighbor - height;
                    result.isCliff[result.GetTileIndex(x, y)] =
                        new ChunkBuildResult.IsCliff(
                            upwardJump > worldGeneration.Elevation.cliffHeight &&
                            height > worldGeneration.Elevation.waterHeight + 0.04f,
                            Mathf.Approximately(highestNeighbor, down) ||
                            Mathf.Approximately(highestNeighbor, up));
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Town surfaces are authoritative terrain overrides. Apply them
            // after cliff classification so the global terrain pass cannot
            // silently turn a rendered house floor back into an unwalkable
            // cliff (or leave it below the navigation water line).
            for (int y = 0; y < ChunkBuildResult.ChunkSize; y++)
            for (int x = 0; x < ChunkBuildResult.ChunkSize; x++)
            {
                int index = result.GetTileIndex(x, y);
                worldGeneration.ApplyTownCell(
                    offsetX + x,
                    offsetY + y,
                    ref result.floorTiles[index],
                    ref result.heights[index],
                    ref result.terrainKinds[index],
                    ref result.isCliff[index],
                    ref result.isRoad[index]);
            }

            cancellationToken.ThrowIfCancellationRequested();
            
            HashSet<Vector2Int> propPositions = new HashSet<Vector2Int>();
            result.props = GeneratePropsForChunk(position, propSpawnRules, propPositions);
            cancellationToken.ThrowIfCancellationRequested();
            AddFeatureBuildings(result);
            worldGeneration.ApplyFeatureEntityGenerators(
                position,
                result.props);
            cancellationToken.ThrowIfCancellationRequested();
            
            return result;

            void AddFeatureBuildings(ChunkBuildResult chunkResult)
            {
                foreach (PropSpawnData building in
                         worldGeneration.GetFeatureBuildingSpawns(position))
                {
                    chunkResult.props.RemoveAll(candidate =>
                        candidate.worldPosition == building.worldPosition);
                    chunkResult.props.Add(building);
                }
            }

            List<PropSpawnData> GeneratePropsForChunk(Vector2Int chunkPosition, IEnumerable<PropSpawnRule> rules, HashSet<Vector2Int> propPositions)
            {
                List<PropSpawnData> props = new List<PropSpawnData>();

                int startX = chunkPosition.x * IChunk.ChunkSize;
                int staryY = chunkPosition.y * IChunk.ChunkSize;

                foreach (var rule in propSpawnRules)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ProcessPropRule(rule, startX, staryY, props, propPositions);
                }
                
                return props;
            }

            void ProcessPropRule(PropSpawnRule rule,
                int startX, int startY,
                List<PropSpawnData> props,
                HashSet<Vector2Int> propPositions)
            {
                int cellSize = Mathf.Max(1, rule.cellSize);
                
                int minCellX = Mathf.FloorToInt((float)startX / cellSize) -1;
                int minCellY = Mathf.FloorToInt((float)startY / cellSize);
                int maxCellX = Mathf.CeilToInt(((float)startX + IChunk.ChunkSize) / cellSize) + 1;
                int maxCellY = Mathf.CeilToInt(((float)startY + IChunk.ChunkSize) / cellSize) + 1;

                for (int cy = minCellY, i = 0; cy < maxCellY; cy++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (int cx = minCellX; cx < maxCellX; cx++,i++)
                    {
                        TrySpawnPropInCell(i,rule, cx, cy, cellSize, startX, startY, props);
                    }
                }
            }

            void TrySpawnPropInCell(
                int slot,
                PropSpawnRule rule,
                int cellX, int cellY,
                int cellSize,
                int chunkStartX, int chunkStartY,
                List<PropSpawnData> props)
            {
                ushort generatorType =
                    Project.Scripts.DataTypes.SaveData.NodeId.CreateGeneratorType(
                        rule.name);
                float chanceRoll = Util.Hash01(
                    cellX,
                    cellY,
                    generatorType);
                
                if(chanceRoll > rule.density)
                    return;
                
                float offsetX = Util.Hash01(cellX, cellY, 1001);
                float offsetY = Util.Hash01(cellX,cellY, 1002);
                
                int worldX = Mathf.FloorToInt((cellX+offsetX) * cellSize);
                int worldY = Mathf.FloorToInt((cellY + offsetY) * cellSize);
                
                if(propPositions.Contains(new Vector2Int(worldX, worldY)))
                    return;
                
                if(worldX < chunkStartX || worldX >= chunkStartX+IChunk.ChunkSize)
                    return;
                
                if(worldY < chunkStartY || worldY >= chunkStartY+IChunk.ChunkSize)
                    return;
                
                int chunkTileIndex = result.GetTileIndex(
                    worldX - chunkStartX,
                    worldY - chunkStartY);
                TerrainSample sample = worldGeneration.GetTerrainSample(
                    worldX,
                    worldY,
                    includePathData: false);
                sample.isRoad = result.isRoad[chunkTileIndex];
                
                if(!CanSpawn(rule, sample, chunkTileIndex))
                    return;
                
                float clusterNoise = worldGeneration.PropNoise(worldX, worldY, rule.noiseScale);
                
                if(clusterNoise < rule.noiseThreshold)
                    return;
                
                float scale = Mathf.Lerp(rule.minScale, rule.maxScale, Util.Hash01(cellX, cellY, 2001));
                bool flipX = rule.randomFlipX && Util.Hash01(cellX, cellY, 2002) > 0.5f;
                
                propPositions.Add(new Vector2Int(worldX, worldY));
                
                var entityId =
                    Project.Scripts.DataTypes.SaveData.NodeId.Create(
                        worldGeneration.Seed,
                        new Vector2Int(worldX, worldY),
                        generatorType,
                        (ushort)slot);
                
                props.Add(new PropSpawnData()
                {
                    NodeId = entityId,
                    propName = rule.name,
                    position = new Vector2(worldX, worldY),
                    scale = scale,
                    flipX = flipX,
                    terrainSample = sample,
                    worldPosition = new Vector2Int(worldX, worldY),
                    persistenceKind = EntityPersistenceKind.Procedural
                });
            }

            bool CanSpawn(
                PropSpawnRule rule,
                TerrainSample sample,
                int chunkTileIndex)
            {
                if(rule.avoidWater && sample.isWater)
                    return false;
                if(sample.terrainKind != TerrainKind.Floor)
                    return false;
                if(rule.avoidCliffs && sample.isCliff)
                    return false;
                if (!rule.canSpawnOnPaths &&
                    restrictedPathCells[chunkTileIndex])
                    return false;
                if(rule.avoidTrails && sample.isTrail)
                    return false;
                if(sample.height < rule.minHeight || sample.height > rule.maxHeight)
                    return false;
                if(sample.temperature < rule.minTemperature || sample.temperature > rule.maxTemperature)
                    return false;
                if(sample.moisture < rule.minMoisture || sample.moisture > rule.maxMoisture)
                    return false;
                if(!BiomeAllowed(rule, sample.biome))
                    return false;
                
                return true;
            }

            bool BiomeAllowed(PropSpawnRule rule, BiomeData biomeName)
            {
                if(rule.allowedBiomes == null || rule.allowedBiomes.Length == 0)
                    return true;
                
                return rule.allowedBiomes.Contains(biomeName);
                
                //TODO: Remove later
                
                bool allowed = false;
                for (int i = 0; i < rule.allowedBiomes.Length; i++)
                {
                    if(rule.allowedBiomes[i] == biomeName)
                        allowed = true;
                }
                
                return allowed;
            }
        }

    }
}
