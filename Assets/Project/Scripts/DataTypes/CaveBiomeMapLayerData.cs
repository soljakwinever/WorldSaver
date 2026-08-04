using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(
        fileName = "Cave Biome Map Layer",
        menuName = "World Generation/Layers/Cave Biome Map")]
    public sealed class CaveBiomeMapLayerData : LayerData
    {
        [Min(32)]
        [Tooltip("Average width and height, in tiles, of a cave biome region.")]
        public int cellSize = 256;

        [Range(0f, 0.45f)]
        [Tooltip("Offsets biome sites from cell centers to avoid a regular grid.")]
        public float siteJitter = 0.38f;

        [Range(0f, 1f)]
        [Tooltip("How strongly continentalness, moisture, and temperature favor suitable biomes. Zero gives every biome equal frequency; one uses climate suitability exclusively.")]
        public float climateInfluence = 0.55f;

        [Min(0f)]
        [Tooltip("Width, in tiles, over which neighboring cave biomes blend at cell borders.")]
        public float edgeBlendWidth = 28f;

        [Tooltip("Additional deterministic salt for this biome map.")]
        public int seedOffset = 9137;
    }
}
