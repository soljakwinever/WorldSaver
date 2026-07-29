using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "Climate Layer", menuName = "World Generation/Layers/Climate")]
    public sealed class ClimateLayerData : LayerData
    {
        [Tooltip("When empty, every BiomeData asset under Resources/Biomes is used.")]
        public BiomeData[] biomes = Array.Empty<BiomeData>();
        [Min(0.01f)] public float continentalNoiseScale = 46.5f;
        [Min(0.01f)] public float moistureNoiseScale = 32f;
        [Min(0.01f)] public float temperatureNoiseScale = 48.2f;
        [Min(0)] public int blendSampleRadius = 2;
    }
}
