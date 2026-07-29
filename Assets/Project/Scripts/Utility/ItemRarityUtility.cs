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

        public static ItemData.Rarity Generate(float roll, int luck)
        {
            float qualityMultiplier =
                1f + Mathf.Max(0, luck - 5) * 0.05f;
            return Generate(Mathf.Clamp01(roll / qualityMultiplier));
        }

        public static ItemData.Rarity Generate(int luck)
        {
            return Generate(Random.value, luck);
        }
        
        public static Color GetRarityColor(Project.Scripts.DataTypes.ItemData.Rarity rarity)
        {
            return rarity switch
            {
                Project.Scripts.DataTypes.ItemData.Rarity.Uncommon => new Color(0.3f, 0.9f, 0.35f),
                Project.Scripts.DataTypes.ItemData.Rarity.Rare => new Color(0.25f, 0.55f, 1f),
                Project.Scripts.DataTypes.ItemData.Rarity.Mythic => new Color(0.75f, 0.3f, 1f),
                Project.Scripts.DataTypes.ItemData.Rarity.Legendary => new Color(1f, 0.6f, 0.1f),
                _ => new Color(0.8f, 0.8f, 0.8f)
            };
        }
    }
}
