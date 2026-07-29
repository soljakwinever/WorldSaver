using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "Lake Layer", menuName = "World Generation/Layers/Lakes")]
    public sealed class LakeLayerData : LayerData
    {
        [Min(0f)] public float depth = 0.1f;
        [Min(0.01f)] public float noiseScale = 4f;
    }
}
