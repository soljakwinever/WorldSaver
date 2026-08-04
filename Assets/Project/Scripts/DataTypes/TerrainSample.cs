using Project.Scripts.DataTypes;

namespace Project.Scripts
{
    public enum TerrainKind : byte
    {
        Floor,
        Wall,
        Crevasse
    }

    public struct TerrainSample
    {
        public TerrainKind terrainKind;
        public float height;
        public float moisture;
        public float temperature;
        public BiomeData biome;
        public BiomeBlend biomeBlend;
            
        public bool isWater;
        public bool isCliff;
        public bool isRoad;
        public bool isTrail;

        public bool IsWalkable =>
            terrainKind == TerrainKind.Floor && !isWater && !isCliff;
    }
}
