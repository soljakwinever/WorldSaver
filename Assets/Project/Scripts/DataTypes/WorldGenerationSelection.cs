using System;

namespace Project.Scripts.DataTypes
{
    public sealed class WorldGenerationSelection
    {
        public int Seed { get; }
        public WorldGenerationPresetData Preset { get; }

        public WorldGenerationSelection(
            int seed,
            WorldGenerationPresetData preset)
        {
            Seed = seed;
            Preset = preset != null
                ? preset
                : throw new ArgumentNullException(nameof(preset));
        }
    }
}
