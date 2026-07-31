#if UNITY_INCLUDE_TESTS
using System.IO;
using NUnit.Framework;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class PersistentInventoryTests
    {
        private GameObject _sourceObject;
        private GameObject _restoredObject;
        private ItemData _wood;
        private ItemData _stone;
        private PersistentInventory _source;
        private PersistentInventory _restored;

        [SetUp]
        public void SetUp()
        {
            _wood = CreateItem("wood", 10);
            _stone = CreateItem("stone", 20);

            _sourceObject = new GameObject("Source inventory");
            _restoredObject = new GameObject("Restored inventory");
            _source = _sourceObject.AddComponent<PersistentInventory>();
            _restored = _restoredObject.AddComponent<PersistentInventory>();

            ItemData[] catalog = { _wood, _stone };
            _source.Configure(4, catalog);
            _restored.Configure(4, catalog);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_sourceObject);
            Object.DestroyImmediate(_restoredObject);
            Object.DestroyImmediate(_wood);
            Object.DestroyImmediate(_stone);
        }

        [Test]
        public void EmptyInventoryIsAtBaseline()
        {
            Assert.That(_source.PersistentTypeId, Is.EqualTo(PersistentInventory.TypeId));
            Assert.That(_source.PersistentVersion, Is.EqualTo(2));
            Assert.That(_source.IsAtBaseline(), Is.True);
        }

        [Test]
        public void StateRoundTripRestoresItemsCountsAndRarities()
        {
            _source.TryAdd(new ItemStack(_wood, 10), out _);
            _source.TryAdd(new ItemStack(_wood, 3), out _);
            _source.TryAdd(new ItemStack(
                _stone,
                4,
                ItemData.Rarity.Rare,
                117), out _);

            byte[] state = WriteState(_source);
            using MemoryStream stream = new(state, writable: false);
            using BinaryReader reader = new(stream);
            _restored.ReadState(reader, _source.PersistentVersion);


            Assert.That(_restored.GetCount(_wood), Is.EqualTo(13));
            Assert.That(_restored.GetCount(_stone, ItemData.Rarity.Rare), Is.EqualTo(4));
            Assert.That(_restored.OccupiedSlots, Is.EqualTo(3));
            Assert.That(_restored.Stacks[2].Item, Is.SameAs(_stone));
            Assert.That(_restored.Stacks[2].Rarity, Is.EqualTo(ItemData.Rarity.Rare));
            Assert.That(_restored.Stacks[2].Count, Is.EqualTo(4));
            Assert.That(_restored.Stacks[2].Durability, Is.EqualTo(117));
            Assert.That(_restored.IsAtBaseline(), Is.False);
        }

        [Test]
        public void RestoringEmptyStateClearsExistingItems()
        {
            _restored.TryAdd(_wood, 5, out _);

            byte[] emptyState = WriteState(_source);
            using MemoryStream stream = new(emptyState, writable: false);
            using BinaryReader reader = new(stream);
            _restored.ReadState(reader, 1);

            Assert.That(_restored.OccupiedSlots, Is.Zero);
            Assert.That(_restored.IsAtBaseline(), Is.True);
        }

        [Test]
        public void VersionOneStacksRestoreAtFullDurability()
        {
            using MemoryStream stream = new();
            using (BinaryWriter writer = new(
                       stream,
                       System.Text.Encoding.UTF8,
                       leaveOpen: true))
            {
                writer.Write(1);
                writer.Write("wood");
                writer.Write((byte)ItemData.Rarity.Common);
                writer.Write(1);
            }

            stream.Position = 0;
            using BinaryReader reader = new(stream);
            _restored.ReadState(reader, 1);

            Assert.That(
                _restored.Stacks[0].Durability,
                Is.EqualTo(byte.MaxValue));
        }

        [Test]
        public void StackOperationsUseBothCountAndRarity()
        {
            ItemStack rareWood = new(_wood, 6, ItemData.Rarity.Rare);
            ItemStack commonWood = new(_wood, 2);
            _source.TryAdd(rareWood, out _);
            _source.TryAdd(commonWood, out _);

            Assert.That(_source.Contains(new ItemStack(_wood, 6, ItemData.Rarity.Rare)), Is.True);
            Assert.That(_source.TryRemove(new ItemStack(_wood, 4, ItemData.Rarity.Rare)), Is.True);
            Assert.That(_source.GetCount(_wood, ItemData.Rarity.Rare), Is.EqualTo(2));
            Assert.That(_source.GetCount(_wood, ItemData.Rarity.Common), Is.EqualTo(2));
        }

        [Test]
        public void UnknownSavedItemIsRejectedWithoutChangingInventory()
        {
            _restored.TryAdd(_wood, 3, out _);

            using MemoryStream stream = new();
            using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(1);
                writer.Write("missing-item");
                writer.Write((byte)ItemData.Rarity.Common);
                writer.Write(1);
            }

            stream.Position = 0;
            using BinaryReader reader = new(stream);

            Assert.That(() => _restored.ReadState(reader, 1), Throws.TypeOf<InvalidDataException>());
            Assert.That(_restored.GetCount(_wood), Is.EqualTo(3));
        }

        [Test]
        public void AddingItemOutsideCatalogIsRejected()
        {
            ItemData unregistered = CreateItem("unregistered", 5);
            try
            {
                ItemStack stack = new(unregistered, 1, ItemData.Rarity.Mythic);
                Assert.That(() => _source.TryAdd(stack, out _),
                    Throws.ArgumentException);
            }
            finally
            {
                Object.DestroyImmediate(unregistered);
            }
        }

        private static ItemData CreateItem(string id, int maxStack)
        {
            ItemData item = ScriptableObject.CreateInstance<ItemData>();
            item.persistentId = id;
            item.maxStack = maxStack;
            return item;
        }

        private static byte[] WriteState(PersistentInventory inventory)
        {
            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream);
            inventory.WriteState(writer);
            return stream.ToArray();
        }
    }
}
#endif
