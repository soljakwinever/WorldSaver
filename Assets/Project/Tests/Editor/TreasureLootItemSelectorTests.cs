#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class TreasureLootItemSelectorTests
    {
        private TreasureItemCatalogData _catalog;
        private ItemData _first;
        private ItemData _second;

        [SetUp]
        public void SetUp()
        {
            _catalog = ScriptableObject.CreateInstance<TreasureItemCatalogData>();
            _first = ScriptableObject.CreateInstance<ItemData>();
            _second = ScriptableObject.CreateInstance<ItemData>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_catalog);
            Object.DestroyImmediate(_first);
            Object.DestroyImmediate(_second);
        }

        [Test]
        public void Select_UsesWeightedBoundaries()
        {
            _catalog.items = new[]
            {
                Entry(_first, 1f),
                Entry(_second, 3f)
            };

            Assert.That(
                TreasureLootItemSelector.Select(_catalog, 0.249),
                Is.SameAs(_first));
            Assert.That(
                TreasureLootItemSelector.Select(_catalog, 0.25),
                Is.SameAs(_second));
        }

        [Test]
        public void Select_IgnoresZeroWeightNullAndInvalidEntries()
        {
            _catalog.items = new[]
            {
                null,
                Entry(_first, 0f),
                Entry(null, 10f),
                Entry(_second, 2f)
            };

            Assert.That(
                TreasureLootItemSelector.Select(_catalog, 0.5),
                Is.SameAs(_second));
        }

        [Test]
        public void Select_ReturnsNullWhenNoEntryHasPositiveWeight()
        {
            _catalog.items = new[]
            {
                Entry(_first, 0f),
                Entry(_second, -1f)
            };

            Assert.That(TreasureLootItemSelector.Select(_catalog, 0.5), Is.Null);
        }

        [Test]
        public void Select_IsDeterministicForTheSameSeed()
        {
            _catalog.items = new[]
            {
                Entry(_first, 1f),
                Entry(_second, 1f)
            };

            ItemData first = TreasureLootItemSelector.Select(
                _catalog, new System.Random(42));
            ItemData second = TreasureLootItemSelector.Select(
                _catalog, new System.Random(42));

            Assert.That(second, Is.SameAs(first));
        }

        private static TreasureItemCatalogEntry Entry(ItemData item, float weight) =>
            new() { item = item, weight = weight };
    }
}
#endif
