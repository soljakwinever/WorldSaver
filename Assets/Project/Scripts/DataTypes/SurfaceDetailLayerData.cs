using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "Surface Detail Layer", menuName = "World Generation/Layers/Surface Details")]
    public sealed class SurfaceDetailLayerData : LayerData
    {
        [Min(0.01f)] public float grassHeightNoiseScale = 10f;
        [Tooltip("Compatibility switch for legacy presets. New presets should disable this and assign their own rules.")]
        public bool useLegacyWorldPropRules = true;
        public PropSpawnRule[] propSpawnRules = Array.Empty<PropSpawnRule>();
    }
}
