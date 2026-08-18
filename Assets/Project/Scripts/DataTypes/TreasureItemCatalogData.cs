using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [Serializable]
    public sealed class TreasureItemCatalogEntry
    {
        public ItemData item;
        [Min(0f)] public float weight = 1f;
    }

    [CreateAssetMenu(
        fileName = "Treasure Item Catalog",
        menuName = "World Generation/Treasure Item Catalog")]
    public sealed class TreasureItemCatalogData : ScriptableObject
    {
        [Tooltip("Items that may be selected and their relative selection weights.")]
        public TreasureItemCatalogEntry[] items =
            Array.Empty<TreasureItemCatalogEntry>();

        [Tooltip("Biomes where this catalog may be selected. Empty makes it an unrestricted fallback catalog.")]
        public BiomeData[] allowedBiomes = Array.Empty<BiomeData>();

        public bool IsUnrestricted =>
            allowedBiomes == null || allowedBiomes.Length == 0;

        public bool ExplicitlyAllows(BiomeData biome)
        {
            if (biome == null || IsUnrestricted)
                return false;

            for (int i = 0; i < allowedBiomes.Length; i++)
            {
                if (allowedBiomes[i] == biome)
                    return true;
            }

            return false;
        }
    }

    public static class TreasureLootItemSelector
    {
        public static ItemData Select(
            TreasureItemCatalogData catalog,
            System.Random random)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            return Select(catalog, random.NextDouble());
        }

        public static ItemData Select(
            TreasureItemCatalogData catalog,
            double roll)
        {
            TreasureItemCatalogEntry[] entries = catalog?.items;
            if (entries == null || entries.Length == 0)
                return null;

            double totalWeight = 0d;
            TreasureItemCatalogEntry lastEligible = null;
            for (int i = 0; i < entries.Length; i++)
            {
                TreasureItemCatalogEntry entry = entries[i];
                if (!IsEligible(entry))
                    continue;

                totalWeight += entry.weight;
                lastEligible = entry;
            }

            if (lastEligible == null || totalWeight <= 0d)
                return null;

            double target = Math.Max(0d, Math.Min(1d, roll)) * totalWeight;
            double cumulative = 0d;
            for (int i = 0; i < entries.Length; i++)
            {
                TreasureItemCatalogEntry entry = entries[i];
                if (!IsEligible(entry))
                    continue;

                cumulative += entry.weight;
                if (target < cumulative)
                    return entry.item;
            }

            return lastEligible.item;
        }

        private static bool IsEligible(TreasureItemCatalogEntry entry) =>
            entry?.item != null &&
            entry.weight > 0f &&
            !float.IsNaN(entry.weight) &&
            !float.IsInfinity(entry.weight);
    }

    public static class TreasureLootCatalogSelector
    {
        public static TreasureItemCatalogData Select(
            TreasureLootConfiguration configuration,
            BiomeData biome,
            System.Random random)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            TreasureItemCatalogData[] catalogs = configuration.catalogs;
            if (catalogs == null || catalogs.Length == 0)
                return null;

            List<TreasureItemCatalogData> matching = new();
            List<TreasureItemCatalogData> unrestricted = new();
            for (int i = 0; i < catalogs.Length; i++)
            {
                TreasureItemCatalogData catalog = catalogs[i];
                if (catalog == null)
                    continue;
                if (catalog.ExplicitlyAllows(biome))
                    matching.Add(catalog);
                else if (catalog.IsUnrestricted)
                    unrestricted.Add(catalog);
            }

            List<TreasureItemCatalogData> eligible =
                matching.Count > 0 ? matching : unrestricted;
            return eligible.Count > 0
                ? eligible[random.Next(eligible.Count)]
                : null;
        }
    }

    [Serializable]
    public readonly struct TreasureLootConfiguration
    {
        public readonly TreasureItemCatalogData[] catalogs;
        public readonly int minimumStacks;
        public readonly int maximumStacks;
        public readonly int minimumItemsPerStack;
        public readonly int maximumItemsPerStack;
        public readonly ItemData.Rarity maximumQuality;

        public bool IsConfigured => catalogs is { Length: > 0 };

        public TreasureLootConfiguration(
            TreasureItemCatalogData[] catalogs,
            int minimumStacks,
            int maximumStacks,
            int minimumItemsPerStack,
            int maximumItemsPerStack,
            ItemData.Rarity maximumQuality)
        {
            this.catalogs = catalogs;
            this.minimumStacks = Mathf.Max(0, minimumStacks);
            this.maximumStacks = Mathf.Max(this.minimumStacks, maximumStacks);
            this.minimumItemsPerStack = Mathf.Max(1, minimumItemsPerStack);
            this.maximumItemsPerStack = Mathf.Max(
                this.minimumItemsPerStack, maximumItemsPerStack);
            this.maximumQuality = maximumQuality;
        }
    }
}
