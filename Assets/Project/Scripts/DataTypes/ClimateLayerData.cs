using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "Climate Layer", menuName = "World Generation/Layers/Climate")]
    public sealed class ClimateLayerData : LayerData
    {
        [Tooltip("Compatibility switch for legacy presets. New presets should disable this and assign their own biomes.")]
        public bool useLegacyResourceBiomes = true;
        public BiomeData[] biomes = Array.Empty<BiomeData>();
        [Min(0.01f)] public float continentalNoiseScale = 46.5f;
        [Min(0.01f)] public float moistureNoiseScale = 32f;
        [Min(0.01f)] public float temperatureNoiseScale = 48.2f;
        [Min(0)] public int blendSampleRadius = 2;
    }
}
