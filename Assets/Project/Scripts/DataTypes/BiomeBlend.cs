using UnityEngine;

namespace Project.Scripts.DataTypes
{
    public struct BiomeBlend
    {
        public BiomeData dominantBiome;

        public float heightOffset;
        public float heightMultiplier;
        public float erosionStrength;
        public float mountainStrength;
        public float valleyStrength;
        public float roughnessStrength;
        
        public float volcanoChance;
        public float craterChance;
        public float mesaChance;
        public float townChance;
        
        public float hillStrength;
        public float hillScale;

        public float bumpStrength;
        public float bumpScale;

        public float cliffChance;
        public float cliffStrength;
        public float cliffScale;
        
        public Color groundColor;
        public Color dirtColor;
        public Color pathColor;
        public Color waterColor;
        public Color cliffColor;
        public Color beachColor;
        public float lakeStrength;
    }
}
