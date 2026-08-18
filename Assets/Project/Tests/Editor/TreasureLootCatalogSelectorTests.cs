#if UNITY_INCLUDE_TESTS
using System;
using NUnit.Framework;
using Project.Scripts;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class TreasureLootCatalogSelectorTests
    {
        private BiomeData _forest;
        private BiomeData _desert;

        [SetUp]
        public void SetUp()
        {
            _forest = ScriptableObject.CreateInstance<BiomeData>();
            _desert = ScriptableObject.CreateInstance<BiomeData>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_forest);
            UnityEngine.Object.DestroyImmediate(_desert);
        }

        [Test]
        public void Select_PrefersMatchingCatalogOverUnrestrictedCatalog()
        {
            TreasureItemCatalogData fallback = CreateCatalog();
            TreasureItemCatalogData forest = CreateCatalog(_forest);
            TreasureLootConfiguration configuration = CreateConfiguration(
                fallback,
                forest);

            TreasureItemCatalogData selected = TreasureLootCatalogSelector.Select(
                configuration,
                _forest,
                new System.Random(123));

            Assert.That(selected, Is.SameAs(forest));
            DestroyCatalogs(fallback, forest);
        }

        [Test]
        public void Select_UsesUnrestrictedCatalogWhenBiomeHasNoMatch()
        {
            TreasureItemCatalogData fallback = CreateCatalog();
            TreasureItemCatalogData forest = CreateCatalog(_forest);

            TreasureItemCatalogData selected = TreasureLootCatalogSelector.Select(
                CreateConfiguration(fallback, forest),
                _desert,
                new System.Random(123));

            Assert.That(selected, Is.SameAs(fallback));
            DestroyCatalogs(fallback, forest);
        }

        [Test]
        public void Select_ReturnsNullWhenNoCatalogIsEligible()
        {
            TreasureItemCatalogData forest = CreateCatalog(_forest);

            TreasureItemCatalogData selected = TreasureLootCatalogSelector.Select(
                CreateConfiguration(null, forest),
                _desert,
                new System.Random(123));

            Assert.That(selected, Is.Null);
            DestroyCatalogs(forest);
        }

        [Test]
        public void Select_IsDeterministicForTheSameSeed()
        {
            TreasureItemCatalogData first = CreateCatalog(_forest);
            TreasureItemCatalogData second = CreateCatalog(_forest);
            TreasureLootConfiguration configuration = CreateConfiguration(
                first,
                second);

            TreasureItemCatalogData firstSelection = TreasureLootCatalogSelector.Select(
                configuration, _forest, new System.Random(9876));
            TreasureItemCatalogData secondSelection = TreasureLootCatalogSelector.Select(
                configuration, _forest, new System.Random(9876));

            Assert.That(secondSelection, Is.SameAs(firstSelection));
            DestroyCatalogs(first, second);
        }

        private static TreasureItemCatalogData CreateCatalog(
            params BiomeData[] allowedBiomes)
        {
            TreasureItemCatalogData catalog =
                ScriptableObject.CreateInstance<TreasureItemCatalogData>();
            catalog.allowedBiomes = allowedBiomes;
            return catalog;
        }

        private static TreasureLootConfiguration CreateConfiguration(
            params TreasureItemCatalogData[] catalogs) =>
            new(catalogs, 1, 1, 1, 1, ItemData.Rarity.Legendary);

        private static void DestroyCatalogs(
            params TreasureItemCatalogData[] catalogs)
        {
            for (int i = 0; i < catalogs.Length; i++)
            {
                if (catalogs[i] != null)
                    UnityEngine.Object.DestroyImmediate(catalogs[i]);
            }
        }
    }
}
#endif
