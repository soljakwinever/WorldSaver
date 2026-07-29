using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "World Generation Preset", menuName = "World Generation/Preset")]
    public sealed class WorldGenerationPresetData : ScriptableObject
    {
        [Tooltip("Stable save identifier. Never reuse an ID for a different preset.")]
        [SerializeField] private string persistentId = "standard";
        [SerializeField] private string displayName = "Standard";
        [Min(1)] [SerializeField] private int version = 1;
        [SerializeField] private bool availableForNewWorlds = true;

        [Header("Debug Preview")]
        public bool heightMapDebug;
        public WorldGenerationPreviewLayer previewLayer =
            WorldGenerationPreviewLayer.Height;

        [Header("Required Layers")]
        public ClimateLayerData climate;
        public ElevationLayerData elevation;
        public LakeLayerData lakes;
        public SmallPoolLayerData smallPools;
        public ValleyLayerData valleys;
        public BiomeMicroTerrainLayerData microTerrain;
        public OutcropLayerData outcrops;
        public FeatureCellLayerData features;
        public SurfaceDetailLayerData surfaceDetails;

        public string PersistentId => persistentId?.Trim() ?? string.Empty;
        public string DisplayName =>
            string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();
        public int Version => Mathf.Max(1, version);
        public bool AvailableForNewWorlds => availableForNewWorlds;
        public bool IsComplete =>
            climate != null &&
            elevation != null &&
            lakes != null &&
            smallPools != null &&
            valleys != null &&
            microTerrain != null &&
            outcrops != null &&
            features != null &&
            surfaceDetails != null;

        private void OnValidate()
        {
            persistentId = persistentId?.Trim();
            displayName = displayName?.Trim();
            version = Mathf.Max(1, version);
        }
    }

    public enum WorldGenerationPreviewLayer
    {
        Height,
        Moisture,
        Temperature,
        PeakValley,
        Lakes,
        SmallPools,
        GrassHeight
    }
}
