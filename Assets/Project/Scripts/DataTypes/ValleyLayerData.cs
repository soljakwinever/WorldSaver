using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "Valley Layer", menuName = "World Generation/Layers/Valleys")]
    public sealed class ValleyLayerData : LayerData
    {
        [Min(0.01f)] public float noiseScale = 32f;
        [Min(0f)] public float depth = 0.22f;
        [Range(0.001f, 1f)] public float width = 0.12f;
    }
}
