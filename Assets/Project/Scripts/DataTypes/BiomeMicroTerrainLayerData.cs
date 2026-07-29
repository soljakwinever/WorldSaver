using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "Micro Terrain Layer", menuName = "World Generation/Layers/Micro Terrain")]
    public sealed class BiomeMicroTerrainLayerData : LayerData
    {
        [Min(0f)] public float hillStrength = 2f;
        [Min(0f)] public float bumpStrength = 1f;
    }
}
