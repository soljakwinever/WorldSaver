using UnityEngine;
using Project.Scripts.DataTypes;

namespace Project.Scripts
{
    public enum BiomePlacement
    {
        Any,
        LandOnly,
        WaterOnly
    }

    [CreateAssetMenu(fileName = "New Biome", menuName = "Biome Data", order = 0)]
    public sealed class BiomeData : ScriptableObject
    {
        public string biomeName;

        [Header("Audio")]
        [Tooltip("Optional medium-priority music while the player is in this biome.")]
        public SongData musicOverride;

        [Header("Map Naming")]
        [Tooltip("Optional deterministic vocabulary used to name this biome's regions on the world map.")]
        public AreaNameParts areaNameParts;

        [Header("Placement")]
        [Tooltip("Water Only biomes are selected after terrain and lakes determine that a cell is underwater. They do not influence terrain height.")]
        public BiomePlacement biomePlacement;
        
        public float temperature;
        public float temperatureVariance;

        public float moisture;
        public float moistureVariance;
            
        public float height;
        public float heightVariance;
            
        public float heightOffset = 0f;
        public float heightMultiplier = 1f;
        public float erosionStrength = 1f;
        public float mountainStrength = 1f;
        public float valleyStrength = 1f;
        public float roughnessStrength = 1f;
            
        [Tooltip("Surface hill strength. In cave layouts this also increases chamber openness.")]
        public float hillStrength = 0.08f;
        public float hillScale = 28f;

        public float bumpStrength = 0.03f;
        public float bumpScale = 9f;

        public float cliffChance = 0.15f;
        public float cliffStrength = 0.08f;
        public float cliffScale = 18f;

        [Header("Cave Layout")]
        [Min(0f)]
        [Tooltip("Multiplier for cave crevasse width and density. Zero disables crevasses; one uses the cave layer defaults.")]
        public float crevasseStrength = 1f;

        public float lakeStrength = 0.25f;
        [Min(0f)]
        public float SmallPoolsStrength = 1f;
        
        public float volcanoChance = 0f;
        public float craterChance = 0f;
        public float mesaChance = 0f;
            
        public float townChance = 0f;
            
        public Color groundColor = Color.green;
        public Color dirtColor = new(0.45f, 0.3f, 0.18f);
        public Color pathColor = Color.lightGoldenRodYellow;
        public Color waterColor = Color.blue;
        public Color cliffColor = Color.red;
        public Color beachColor = Color.sandyBrown;

        [Header("Seasonal Tint")]
        public Color springTint = Color.white;
        public Color summerTint = Color.white;
        public Color fallTint = Color.white;
        public Color winterTint = Color.white;

        [Min(0f)]
        public float SeasonColorEffectMod = 1f;
        
        public TileData overrideGroundTile;
        public TileData overridePathTile;
        [Tooltip("Biome water tile. Assigning this on a cave biome enables underground water in sufficiently low floor terrain.")]
        public TileData overrideWaterTile;
        public TileData overrideCliffTile;
        [Tooltip("Biome beach tile. Assigning this on a cave biome enables beach terrain around underground lakes and pools.")]
        public TileData overrideBeachTile;

        [Header("Tile Actions")]
        [Tooltip("Tile placed after mining in this biome. Leave empty to use the WorldData default.")]
        public TileData overrideMinedTileReplacement;

    }
}
