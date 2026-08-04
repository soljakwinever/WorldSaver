using System;
using Project.Scripts.Enums;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "Plant", menuName = "Data/Plant")]
    public sealed class PlantData : ScriptableObject
    {
        [Serializable]
        public struct GrowthStage
        {
            [Range(0f, 1f)] public float progress;
            public Sprite sprite;
        }

        [Serializable]
        public struct HarvestYield
        {
            public ItemData item;
            [Min(1)] public int minimumQuantity;
            [Min(1)] public int maximumQuantity;
        }

        public EntityTag[] plantableTileTags = Array.Empty<EntityTag>();
        public Season[] growingSeasons = Array.Empty<Season>();
        [Min(0.01f)] public float hoursToMature = 24f;
        [Min(0f)] public float waterPointsPerHour = 1f;
        [Min(0.01f)] public float maximumWaterPoints = 24f;
        [Min(1)] public int maximumHealth = 100;
        [Min(0f)] public float healthLostPerDryHour = 1f;
        [Min(0f)] public float healthLostPerInvalidSeasonHour = 1f;
        public GrowthStage[] growthStages = Array.Empty<GrowthStage>();
        public Sprite wiltedSprite;
        public HarvestYield[] yields = Array.Empty<HarvestYield>();
        public bool multipleHarvests;
        [Range(0f, 1f)] public float progressAfterHarvest = 0.5f;

        public bool CanGrowIn(Season season)
        {
            for (int i = 0; i < (growingSeasons?.Length ?? 0); i++)
                if (growingSeasons[i] == season) return true;
            return false;
        }

        public bool CanPlantOn(TileData tile)
        {
            if (tile == null) return false;
            for (int i = 0; i < (plantableTileTags?.Length ?? 0); i++)
                if (tile.HasTag(plantableTileTags[i])) return true;
            return false;
        }
    }
}
