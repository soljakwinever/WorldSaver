using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "Outcrop Layer", menuName = "World Generation/Layers/Outcrops")]
    public sealed class OutcropLayerData : LayerData
    {
        [Min(0f)] public float strength = 2f;
        [Range(0f, 1f)] public float threshold = 0.25f;
        [Min(0.01f)] public float noiseScale = 2f;
    }
}
