using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    public class ChunkGenerator
    {
        public bool IsRunning { get; private set; }
        
        private readonly Queue<Vector2Int> pendingChunks = new Queue<Vector2Int>();
        private readonly List<Task<ChunkBuildResult>> runningTasks = new List<Task<ChunkBuildResult>>();
        private readonly Queue<ChunkBuildResult> completedChunks = new ();
        
        public int maxConcurrentTasks = 2;
        public int maxChunksAppliedPerFrame = 1;

        private readonly HashSet<Vector2Int> queuedOrRunning = new();
        private readonly HashSet<Vector2Int> completed = new();
        
        [Inject] private WorldData worldData;
        [Inject] private WorldGeneration worldGeneration;
        [Inject] private Chunk.Pool chunkPool;

        public void RequestChunk(Vector2Int position)
        {
            if(completed.Contains(position))
                return;
            
            if(queuedOrRunning.Contains(position))
                return;
            
            pendingChunks.Enqueue(position);
            queuedOrRunning.Add(position);
        }

        public async Awaitable Run(Chunkloader chunkloader, CancellationToken cancellationToken)
        {
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
                    
                    var chunk = chunkPool.Spawn(result);
                    chunkloader.ReportSpawn(chunk);
                    
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

                Task<ChunkBuildResult> task = Task.Run(() => BuildChunk(chunkPosition, worldGeneration, worldData.propSpawnRules, cancellationToken), cancellationToken);

                runningTasks.Add(task);
            }
        }

        private static async Task<ChunkBuildResult> BuildChunk(Vector2Int position, WorldGeneration worldGeneration, WorldData.PropSpawnRule[] propSpawnRules, CancellationToken cancellationToken = default)
        {
            ChunkBuildResult result = new ChunkBuildResult(position);

            int offsetX = position.x * Chunk.ChunkSize;
            int offsetY = position.y * Chunk.ChunkSize;

            for (int y = 0; y < Chunk.ChunkSize; y++)
            {
                for (int x = 0; x < Chunk.ChunkSize; x++)
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

            for (int y = 1; y < Chunk.ChunkSize - 1; y++)
            {
                for (int x = 1; x < Chunk.ChunkSize -1; x++)
                {
                    int index = result.GetTileIndex(x, y);

                    result.isCliff[index] = worldGeneration.IsSmallCliff(x, y);
                    
                    if(cancellationToken.IsCancellationRequested)
                        break;
                }
                if(cancellationToken.IsCancellationRequested)
                    break;
            }
            
            result.props = GeneratePropsForChunk(position, propSpawnRules);
            
            return result;

            List<PropSpawnData> GeneratePropsForChunk(Vector2Int chunkPosition, WorldData.PropSpawnRule[] rules)
            {
                List<PropSpawnData> props = new List<PropSpawnData>();

                int startX = chunkPosition.x * Chunk.ChunkSize;
                int staryY = chunkPosition.y * Chunk.ChunkSize;

                foreach (var rule in propSpawnRules)
                {
                    ProcessPropRule(rule, startX, staryY, props);
                }
                
                return props;
            }

            void ProcessPropRule(WorldData.PropSpawnRule rule,
                int startX, int startY,
                List<PropSpawnData> props)
            {
                int cellSize = Mathf.Max(1, rule.cellSize);
                
                int minCellX = Mathf.FloorToInt((float)startX / cellSize) -1;
                int minCellY = Mathf.FloorToInt((float)startY / cellSize);
                int maxCellX = Mathf.CeilToInt(((float)startX + Chunk.ChunkSize) / cellSize) + 1;
                int maxCellY = Mathf.CeilToInt(((float)startY + Chunk.ChunkSize) / cellSize) + 1;

                for (int cy = minCellY; cy < maxCellY; cy++)
                {
                    for (int cx = minCellX; cx < maxCellX; cx++)
                    {
                        TrySpawnPropInCell(rule, cx, cy, cellSize, startX, startY, props);
                    }
                }
            }

            void TrySpawnPropInCell(
                WorldData.PropSpawnRule rule,
                int cellX, int cellY,
                int cellSize,
                int chunkStartX, int chunkStartY,
                List<PropSpawnData> props)
            {
                float chanceRoll = WorldGeneration.Hash01(cellX, cellY, rule.name.GetHashCode());
                
                if(chanceRoll > rule.density)
                    return;
                
                float offsetX = WorldGeneration.Hash01(cellX, cellY, 1001);
                float offsetY = WorldGeneration.Hash01(cellX,cellY, 1002);
                
                int worldX = Mathf.FloorToInt((cellX+offsetX) * cellSize);
                int worldY = Mathf.FloorToInt((cellY + offsetY) * cellSize);
                
                if(worldX < chunkStartX || worldX >= chunkStartX+Chunk.ChunkSize)
                    return;
                
                if(worldY < chunkStartY || worldY >= chunkStartY+Chunk.ChunkSize)
                    return;
                
                TerrainSample sample = worldGeneration.GetTerrainSample(worldX, worldY);
                
                if(!CanSpawn(rule, sample))
                    return;
                
                float clusterNoise = worldGeneration.PropNoise(worldX, worldY, rule.noiseScale);
                
                if(clusterNoise < rule.noiseThreshold)
                    return;
                
                float scale = Mathf.Lerp(rule.minScale, rule.maxScale, WorldGeneration.Hash01(cellX, cellY, 2001));
                bool flipX = rule.randomFlipX && WorldGeneration.Hash01(cellX, cellY, 2002) > 0.5f;
                
                props.Add(new PropSpawnData()
                {
                    propName = rule.name,
                    position = new Vector2(worldX, worldY),
                    scale = scale,
                    flipX = flipX
                });
            }

            bool CanSpawn(WorldData.PropSpawnRule rule, TerrainSample sample)
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
                if(!BiomeAllowed(rule, sample.biome.biomeName))
                    return false;
                
                return true;
            }

            bool BiomeAllowed(WorldData.PropSpawnRule rule, string biomeName)
            {
                if(rule.allowedBiomes == null || rule.allowedBiomes.Length == 0)
                    return true;

                for (int i = 0; i < rule.allowedBiomes.Length; i++)
                {
                    if(rule.allowedBiomes[i] == biomeName)
                        return true;
                }
                return false;
            }
        }

        public struct TerrainSample
        {
            public float height;
            public float moisture;
            public float temperature;
            public BiomeData biome;
            
            public bool isWater;
            public bool isCliff;
            public bool isRoad;
            public bool isTrail;
        }
        
        public struct PropSpawnData
        {
            public string propName;
            public Vector2 position;
            public float scale;
            public bool flipX;
        }
        
        public class ChunkBuildResult
        {
            public Vector2Int chunkPosition;

            public int[] tileIndexes;
            public float[] heights;
            public float[] moisture;
            public float[] temperature;
            public BiomeSelector.BiomeBlend[] biomeData;

            public bool[] isCliff;
            public bool[] isRoad;
            public bool[] isTrail;
            public List<PropSpawnData> props = new();

            public ChunkBuildResult(Vector2Int chunkPosition)
            {
                this.chunkPosition = chunkPosition;
                tileIndexes = new int[Chunk.ChunkSize * Chunk.ChunkSize];
                heights = new float[Chunk.ChunkSize * Chunk.ChunkSize];
                moisture = new float[Chunk.ChunkSize * Chunk.ChunkSize];
                temperature = new float[Chunk.ChunkSize * Chunk.ChunkSize];
                isCliff = new bool[Chunk.ChunkSize * Chunk.ChunkSize];
                isRoad = new bool[Chunk.ChunkSize * Chunk.ChunkSize];
                isTrail = new bool[Chunk.ChunkSize * Chunk.ChunkSize];
                biomeData = new BiomeSelector.BiomeBlend[Chunk.ChunkSize * Chunk.ChunkSize];
            }

            public int GetTileIndex(int x, int y)
            {
                return x + y * Chunk.ChunkSize;
            }
        }
    }
}