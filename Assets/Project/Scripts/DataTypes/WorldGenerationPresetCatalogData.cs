using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "PresetCatalog", menuName = "World Generation/Preset Catalog")]
    public sealed class WorldGenerationPresetCatalogData : ScriptableObject
    {
        public WorldGenerationPresetData newWorldDefault;
        [Tooltip("Used for manifests created before preset identity was saved.")]
        public WorldGenerationPresetData legacyFallback;
        public WorldGenerationPresetData[] presets =
            Array.Empty<WorldGenerationPresetData>();

        public bool TryResolve(
            string persistentId,
            int version,
            out WorldGenerationPresetData preset)
        {
            preset = null;
            if (string.IsNullOrWhiteSpace(persistentId))
            {
                preset = legacyFallback != null
                    ? legacyFallback
                    : newWorldDefault;
                return preset != null;
            }

            foreach (WorldGenerationPresetData candidate in
                     presets ?? Array.Empty<WorldGenerationPresetData>())
            {
                if (Matches(candidate, persistentId, version))
                {
                    preset = candidate;
                    return true;
                }
            }

            if (Matches(newWorldDefault, persistentId, version))
                preset = newWorldDefault;
            else if (Matches(legacyFallback, persistentId, version))
                preset = legacyFallback;
            return preset != null;
        }

        private static bool Matches(
            WorldGenerationPresetData candidate,
            string persistentId,
            int version) =>
            candidate != null &&
            string.Equals(
                candidate.PersistentId,
                persistentId,
                StringComparison.Ordinal) &&
            candidate.Version == Mathf.Max(1, version);
    }
}
