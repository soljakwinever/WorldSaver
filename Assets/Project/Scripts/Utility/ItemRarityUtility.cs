using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Utility
{
    public static class ItemRarityUtility
    {
        public const float UncommonChance = 0.10f;
        public const float RareChance = 0.02f;
        public const float MythicChance = 0.005f;
        public const float LegendaryChance = 0.0002f;

        public static ItemData.Rarity Generate()
        {
            return Generate(Random.value);
        }

        public static ItemData.Rarity Generate(float roll)
        {
            roll = Mathf.Clamp01(roll);

            if (roll < LegendaryChance)
                return ItemData.Rarity.Legendary;

            roll -= LegendaryChance;
            if (roll < MythicChance)
                return ItemData.Rarity.Mythic;

            roll -= MythicChance;
            if (roll < RareChance)
                return ItemData.Rarity.Rare;

            roll -= RareChance;
            return roll < UncommonChance
                ? ItemData.Rarity.Uncommon
                : ItemData.Rarity.Common;
        }
    }
}
