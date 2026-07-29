using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "Small Pool Layer", menuName = "World Generation/Layers/Small Pools")]
    public sealed class SmallPoolLayerData : LayerData
    {
        [Min(0.01f)] public float noiseScale = 7f;
        [Min(0f)] public float strength = 0.06f;
    }
}
