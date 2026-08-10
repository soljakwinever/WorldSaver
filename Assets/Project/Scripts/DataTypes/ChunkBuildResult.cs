using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;

namespace Project.Scripts
{
    public sealed class ChunkBuildResult
    {
        public const int ChunkSize = 32;
        public const int BufferedSize = ChunkSize + OverbufferSize * 2;
        public const int OverbufferSize = 2;
        
        public Vector2Int chunkPosition;

        public int[] tileIndexes;
        public float[] heights;
        public float[] moisture;
        public float[] temperature;
        public BiomeBlend[] biomeData;
        public TileData[] floorTiles;
        public TerrainKind[] terrainKinds;

        public IsCliff[] isCliff;
        public bool[] isRoad;
        public bool[] isTrail;
        public List<PropSpawnData> props = new();
        
        public readonly struct IsCliff
        {
            public readonly bool cliff;
            public readonly bool wall;

            public IsCliff(bool cliff, bool wall)
            {
                this.cliff = cliff;
                this.wall = wall;
            }
            
            public static implicit operator bool(IsCliff cliffType)
            {
                return cliffType.cliff;
            }
        }
        
        public ChunkBuildResult(Vector2Int chunkPosition)
        {
            this.chunkPosition = chunkPosition;
            
            tileIndexes = new int[ChunkSize * ChunkSize];
            heights = new float[ChunkSize * ChunkSize];
            moisture = new float[ChunkSize * ChunkSize];
            temperature = new float[ChunkSize * ChunkSize];
            isCliff = new IsCliff[ChunkSize * ChunkSize];
            isRoad = new bool[ChunkSize * ChunkSize];
            isTrail = new bool[ChunkSize * ChunkSize];
            biomeData = new BiomeBlend[ChunkSize * ChunkSize];
            floorTiles = new TileData[ChunkSize * ChunkSize];
            terrainKinds = new TerrainKind[ChunkSize * ChunkSize];
        }

        public int GetTileIndex(int x, int y)
        {
            return (x) + (y) * ChunkSize;
        }

        public TerrainSample GetTerrainSample(int x, int y)
        {
            return new TerrainSample()
            {
                biome = biomeData[GetTileIndex(x, y)].dominantBiome,
                height = heights[GetTileIndex(x, y)],
                moisture = moisture[GetTileIndex(x, y)],
                temperature = temperature[GetTileIndex(x, y)],
                terrainKind = terrainKinds[GetTileIndex(x, y)],

                isCliff = isCliff[GetTileIndex(x, y)],
                isRoad = isRoad[GetTileIndex(x, y)],
                isTrail = isTrail[GetTileIndex(x, y)],
            };
        }
    }
    
            
    public struct PropSpawnData
    {
        public Project.Scripts.DataTypes.SaveData.NodeId NodeId;
        public Vector2Int worldPosition;
        public string propName;
        public NodeData nodeData;
        public Vector2 position;
        public float scale;
        public bool flipX;
        public TerrainSample terrainSample;
        public EntityPersistenceKind persistenceKind;
        public AccessIdentity accessIdentity;
        public bool damageImmune;
        public bool clearReservedAreaCoverage;
        public string generatedTownName;
    }
}
