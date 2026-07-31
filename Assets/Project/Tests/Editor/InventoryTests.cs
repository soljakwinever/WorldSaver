#if UNITY_INCLUDE_TESTS
using System;
using System.Reflection;
using NUnit.Framework;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Project.Tests.EditMode
{
    public sealed class InventoryTests
    {
        private ItemData _item;
        private IInventory _inventory;

        [SetUp]
        public void SetUp()
        {
            _item = ScriptableObject.CreateInstance<ItemData>();
            _item.maxStack = 10;
            _inventory = new Inventory(2);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_item);
        }

        [Test]
        public void AddFillsExistingStackBeforeCreatingAnother()
        {
            Assert.That(_inventory.TryAdd(_item, 6, out int firstRemainder), Is.True);
            Assert.That(_inventory.TryAdd(_item, 7, out int secondRemainder), Is.True);


            Assert.That(firstRemainder, Is.Zero);
            Assert.That(secondRemainder, Is.Zero);
            Assert.That(_inventory.OccupiedSlots, Is.EqualTo(2));
            Assert.That(_inventory.Stacks[0].Count, Is.EqualTo(10));
            Assert.That(_inventory.Stacks[0].IsFull, Is.True);
            Assert.That(_inventory.Stacks[1].Count, Is.EqualTo(3));
            Assert.That(_inventory.GetCount(_item), Is.EqualTo(13));
        }

        [Test]
        public void AddReportsItemsThatDoNotFit()
        {
            bool addedAll = _inventory.TryAdd(_item, 25, out int remainder);


            Assert.That(addedAll, Is.False);
            Assert.That(remainder, Is.EqualTo(5));
            Assert.That(_inventory.GetCount(_item), Is.EqualTo(20));
            Assert.That(_inventory.OccupiedSlots, Is.EqualTo(_inventory.Size));
        }

        [Test]
        public void DifferentRaritiesUseDifferentStacks()
        {
            _inventory.TryAdd(_item, 4, out _);
            _inventory.TryAdd(_item, 3, out _, ItemData.Rarity.Rare);


            Assert.That(_inventory.GetCount(_item, ItemData.Rarity.Common), Is.EqualTo(4));
            Assert.That(_inventory.GetCount(_item, ItemData.Rarity.Rare), Is.EqualTo(3));
            Assert.That(_inventory.OccupiedSlots, Is.EqualTo(2));
        }

        [Test]
        public void DifferentDurabilityValuesUseDifferentStacks()
        {
            _inventory.TryAdd(
                _item, 1, out _, ItemData.Rarity.Rare, 255);
            _inventory.TryAdd(
                _item, 1, out _, ItemData.Rarity.Rare, 128);

            Assert.That(_inventory.OccupiedSlots, Is.EqualTo(2));
            Assert.That(_inventory.Stacks[0].Durability, Is.EqualTo(255));
            Assert.That(_inventory.Stacks[1].Durability, Is.EqualTo(128));
            Assert.That(
                _inventory.Contains(new ItemStack(
                    _item, 1, ItemData.Rarity.Rare, 128)),
                Is.True);
        }

        [Test]
        public void RemovingAStackMatchesDurability()
        {
            _inventory.TryAdd(
                _item, 1, out _, ItemData.Rarity.Common, 255);
            _inventory.TryAdd(
                _item, 1, out _, ItemData.Rarity.Common, 64);

            Assert.That(
                _inventory.TryRemove(new ItemStack(
                    _item, 1, ItemData.Rarity.Common, 64)),
                Is.True);
            Assert.That(_inventory.OccupiedSlots, Is.EqualTo(1));
            Assert.That(_inventory.Stacks[0].Durability, Is.EqualTo(255));
        }

        [Test]
        public void DurabilityDamageClampsAtBroken()
        {
            ItemStack stack = new(_item, 1, durability: 10);

            Assert.That(stack.ApplyDurabilityDamage(15), Is.EqualTo(10));
            Assert.That(stack.Durability, Is.Zero);
            Assert.That(stack.Durability01, Is.Zero);
            Assert.That(stack.IsBroken, Is.True);
        }

        [Test]
        public void RemoveSpansStacksAndRemovesEmptySlots()
        {
            _inventory.TryAdd(_item, 16, out _);

            bool removed = _inventory.TryRemove(_item, 12);


            Assert.That(removed, Is.True);
            Assert.That(_inventory.GetCount(_item), Is.EqualTo(4));
            Assert.That(_inventory.OccupiedSlots, Is.EqualTo(1));
            Assert.That(_inventory.Stacks[0].Count, Is.EqualTo(4));
        }

        [Test]
        public void RemoveIsAtomicWhenThereAreNotEnoughItems()
        {
            _inventory.TryAdd(_item, 5, out _);

            Assert.That(_inventory.TryRemove(_item, 6), Is.False);
            Assert.That(_inventory.GetCount(_item), Is.EqualTo(5));
        }

        [Test]
        public void ContainsAndClearReflectInventoryContents()
        {
            _inventory.TryAdd(_item, 5, out _);

            Assert.That(_inventory.Contains(_item, 5), Is.True);
            Assert.That(_inventory.Contains(_item, 6), Is.False);

            _inventory.Clear();

            Assert.That(_inventory.OccupiedSlots, Is.Zero);
            Assert.That(_inventory.Contains(_item), Is.False);
        }

        [Test]
        public void ContainsByTagOnlyCountsRequestedRarity()
        {
            EntityTag tag = ScriptableObject.CreateInstance<EntityTag>();
            try
            {
                typeof(ItemData)
                    .GetField("tags", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(_item, new[] { tag });
                _inventory.TryAdd(_item, 4, out _, ItemData.Rarity.Common);
                _inventory.TryAdd(_item, 3, out _, ItemData.Rarity.Rare);

                Assert.That(_inventory.Contains(tag, 4, ItemData.Rarity.Common), Is.True);
                Assert.That(_inventory.Contains(tag, 5, ItemData.Rarity.Common), Is.False);
                Assert.That(_inventory.Contains(tag, 3, ItemData.Rarity.Rare), Is.True);
                Assert.That(_inventory.Contains(tag, 4, ItemData.Rarity.Rare), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(tag);
            }
        }

        [Test]
        public void InvalidConstructionAndCountsAreRejected()
        {
            Assert.That(() => new Inventory(0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => _inventory.TryAdd(_item, 0, out _),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => _inventory.TryRemove(_item, 0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => _inventory.Contains(_item, 0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => _inventory.Contains((EntityTag)null),
                Throws.TypeOf<ArgumentNullException>());
        }
    }
}
#endif
