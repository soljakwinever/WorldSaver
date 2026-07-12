using Project.Scripts.DataTypes;

namespace Project.Scripts
{
    public struct TerrainSample
    {
        public float height;
        public float moisture;
        public float temperature;
        public BiomeData biome;
        public BiomeBlend biomeBlend;
            
        public bool isWater;
        public bool isCliff;
        public bool isRoad;
        public bool isTrail;
    }
}