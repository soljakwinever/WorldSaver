using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "Feature Cell Layer", menuName = "World Generation/Layers/Feature Cells")]
    public sealed class FeatureCellLayerData : LayerData
    {
        [Min(1)] public int cellSize = 256;
        [Range(0f, 1f)] public float chancePerCell = 0.15f;
        public FeatureData[] features = Array.Empty<FeatureData>();
    }
}
