using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    public enum AreaNameStyle
    {
        Compound,
        Preset
    }

    [CreateAssetMenu(
        fileName = "Area Name Parts",
        menuName = "World Generation/Area Name Parts")]
    public sealed class AreaNameParts : ScriptableObject
    {
        [Tooltip("Preset selects a complete name. Compound builds a name from one to three component pools.")]
        public AreaNameStyle style;

        [Header("Preset Names")]
        public string[] presetNames = Array.Empty<string>();

        [Header("Compound Names")]
        [Tooltip("First component, such as Ash, Green, or King's.")]
        public string[] firstParts = Array.Empty<string>();
        [Tooltip("Optional second component, such as en, ing, or Shadow.")]
        public string[] middleParts = Array.Empty<string>();
        [Tooltip("Final component, such as wood, vale, or reach.")]
        public string[] lastParts = Array.Empty<string>();
        [Tooltip("Separators used between components. Use an empty entry to append directly, a space, apostrophe, or hyphen.")]
        public string[] separators = { "", " ", "'", "-" };
        [Range(1, 3)] public int minimumComponents = 2;
        [Range(1, 3)] public int maximumComponents = 3;

        [Header("Feature Type")]
        [Tooltip("Biome-specific nouns appended to the generated proper name, such as Hills, Slopes, or Fen.")]
        public string[] featureTypes = Array.Empty<string>();

        private void OnValidate()
        {
            minimumComponents = Mathf.Clamp(minimumComponents, 1, 3);
            maximumComponents = Mathf.Clamp(maximumComponents,
                minimumComponents, 3);
        }
    }
}
