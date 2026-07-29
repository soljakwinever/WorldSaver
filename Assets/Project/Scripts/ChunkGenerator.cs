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
        private readonly List<Task<ChunkBuildResult>> runningTasks = new List<Task<ChunkBuildResult>>();
        private readonly Queue<ChunkBuildResult> completedChunks = new ();
        
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
                StartPendingTasks(cancellationToken);
                CollectCompletedTasks();

                int applied = 0;

                while (completedChunks.Count > 0 && applied < maxChunksAppliedPerFrame && !cancellationToken.IsCancellationRequested)
                {
                    var result = completedChunks.Dequeue();
                    applied++;

                    if(cancellationToken.IsCancellationRequested) break;
                    
                    mapSignalBus.RaiseChunkBuilt(result);
                    //chunkloader.ReportSpawn(chunk);
                    
                    completed.Add(result.chunkPosition);
                    queuedOrRunning.Remove(result.chunkPosition);

                    applied++;
                }

                await Awaitable.NextFrameAsync(cancellationToken);
            } while (!cancellationToken.IsCancellationRequested);

            void CollectCompletedTasks()
            {
                for (int i = runningTasks.Count - 1; i >= 0; i--)
                {
                    Task<ChunkBuildResult> task = runningTasks[i];
                    
                    if(!task.IsCompleted)
                        continue;
                    
                    runningTasks.RemoveAt(i);

                    if (task.IsFaulted)
                    {
                        Debug.LogException(task.Exception);
                        continue;
                    }
                    
                    completedChunks.Enqueue(task.Result);
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
            if (pendingChunks.Count > 0 && runningTasks.Count < maxConcurrentTasks)
            {
                var chunkPosition = pendingChunks.Dequeue();
                
                //Todo: World Snapshot

                var propSpawnRules = worldGeneration.PropSpawnRules
                    .ShuffleXY(chunkPosition.x, chunkPosition.y)
                    .ToArray();
                
                Task<ChunkBuildResult> task = Task.Run(() => BuildChunk(chunkPosition, worldGeneration, propSpawnRules, cancellationToken), cancellationToken);

                runningTasks.Add(task);
            }
        }

        private static async Task<ChunkBuildResult> BuildChunk(Vector2Int position, WorldGeneration worldGeneration, IEnumerable<PropSpawnRule> propSpawnRules, CancellationToken cancellationToken = default)
        {
            ChunkBuildResult result = new ChunkBuildResult(position);

            int offsetX = position.x * ChunkBuildResult.ChunkSize;
            int offsetY = position.y * ChunkBuildResult.ChunkSize;

            for (int y = 0; y < ChunkBuildResult.ChunkSize; y++)
            {
                for (int x = 0; x < ChunkBuildResult.ChunkSize; x++)
                {
                    var worldX = offsetX + x;
                    var worldY = offsetY + y;
                    
                    int index = result.GetTileIndex(x, y);

                    int tileIndex = worldGeneration.GetTile(
                        worldX,
                        worldY,
                        out result.biomeData[index],
                        out result.heights[index],
                        out result.moisture[index],
                        out result.temperature[index]);
                    
                    result.tileIndexes[index] = tileIndex;
                    
                    if(cancellationToken.IsCancellationRequested)
                        break;
                }
                if(cancellationToken.IsCancellationRequested)
                    break;
            }

            for (int y = 0; y < IChunk.ChunkSize; y++)
            {
                for (int x = 0; x < IChunk.ChunkSize; x++)
                {
                    int index = result.GetTileIndex(x, y);

                    result.isCliff[index] = worldGeneration.IsSmallCliff(offsetX+x, offsetY+y);
                    
                    if(cancellationToken.IsCancellationRequested)
                        break;
                }
                if(cancellationToken.IsCancellationRequested)
                    break;
            }
            
            HashSet<Vector2Int> propPositions = new HashSet<Vector2Int>();
            result.props = GeneratePropsForChunk(position, propSpawnRules, propPositions);
            
            return result;

            List<PropSpawnData> GeneratePropsForChunk(Vector2Int chunkPosition, IEnumerable<PropSpawnRule> rules, HashSet<Vector2Int> propPositions)
            {
                List<PropSpawnData> props = new List<PropSpawnData>();

                int startX = chunkPosition.x * IChunk.ChunkSize;
                int staryY = chunkPosition.y * IChunk.ChunkSize;

                foreach (var rule in propSpawnRules)
                {
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
                
                TerrainSample sample = worldGeneration.GetTerrainSample(worldX, worldY);
                
                if(!CanSpawn(rule, sample))
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

            bool CanSpawn(PropSpawnRule rule, TerrainSample sample)
            {
                if(rule.avoidWater && sample.isWater)
                    return false;
                if(rule.avoidCliffs && sample.isCliff)
                    return false;
                if(rule.avoidRoads && sample.isRoad)
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
