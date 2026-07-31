using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    /// <summary>
    /// Implemented by loaded world-tile owners that can expose damaged
    /// structures without coupling town effects to the root Chunk assembly.
    /// </summary>
    public interface ITownTileRepairSource
    {
        int GetMaximumMissingHealth(Vector2 townCenter, float townRadius);

        int RepairDamagedTiles(
            Vector2 townCenter,
            float townRadius,
            int healthPerTile);
    }

    public static class TownTileRepairRegistry
    {
        private static readonly HashSet<ITownTileRepairSource> Sources = new();

        public static void Register(ITownTileRepairSource source)
        {
            if (source != null)
                Sources.Add(source);
        }

        public static void Unregister(ITownTileRepairSource source)
        {
            if (source != null)
                Sources.Remove(source);
        }

        public static void CopySourcesTo(List<ITownTileRepairSource> results)
        {
            results.Clear();
            results.AddRange(Sources);
        }
    }
}
