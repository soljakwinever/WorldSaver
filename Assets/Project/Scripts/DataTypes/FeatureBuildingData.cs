using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(
        fileName = "Feature Building",
        menuName = "World Generation/Feature Building")]
    public sealed class FeatureBuildingData : FeatureData
    {
        [Header("Building Entity")]
        [Tooltip("Archetype used by both deterministic world generation and runtime spawning.")]
        public EntityArchetype entityArchetype;

        [Tooltip("Only one building with the same non-empty key may exist in a region.")]
        public string regionUniqueKey = "ClimateCore";

        [Tooltip("Offset from the generated feature center, in world cells.")]
        public Vector2 placementOffset;

        public bool IsConfigured =>
            entityArchetype != null && entityArchetype.NodeData != null;

        protected override void OnValidate()
        {
            base.OnValidate();
            regionUniqueKey = regionUniqueKey?.Trim();
        }
    }
}
