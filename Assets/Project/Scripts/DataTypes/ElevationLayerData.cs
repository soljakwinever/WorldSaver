using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "Elevation Layer", menuName = "World Generation/Layers/Elevation")]
    public sealed class ElevationLayerData : LayerData
    {
        [Range(0f, 1f)] public float waterHeight = 0.2f;
        [Range(0f, 1f)] public float beachHeight = 0.3f;
        [Range(0f, 1f)] public float mountainHeight = 0.5f;
        [Min(0f)] public float cliffHeight = 0.005f;
        [Min(0.01f)] public float erosionNoiseScale = 0.5f;
        [Min(0.01f)] public float peakValleyNoiseScale = 8.5f;
        public float normalizationMinimum = -0.1f;
        public float normalizationMaximum = 1.25f;

        private void OnValidate()
        {
            beachHeight = Mathf.Max(waterHeight, beachHeight);
            mountainHeight = Mathf.Max(beachHeight, mountainHeight);
            if (normalizationMaximum <= normalizationMinimum)
                normalizationMaximum = normalizationMinimum + 0.01f;
        }
    }
}
