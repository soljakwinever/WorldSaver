using System;
using System.Collections.Generic;
using System.IO;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class TownStockpile : MonoBehaviour, IInventory,
        IPersistentComponent
    {
        public const ushort TypeId = 0x5453; // TS
        private const ushort Version = 2;
        [SerializeField, Min(1)] private int size = 32;
        private Inventory _inventory;
        private ItemCatalog _catalog;

        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => Version;
        public int Size => Value.Size;
        public int OccupiedSlots => Value.OccupiedSlots;
        public IReadOnlyList<IItemStack> Stacks => Value.Stacks;
        private Inventory Value => _inventory ??= new Inventory(size);

        [Inject]
        public void Construct(ItemCatalog catalog) =>
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

        public void Initialize(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (_inventory != null && _inventory.OccupiedSlots > 0)
                throw new InvalidOperationException("A non-empty stockpile cannot be resized.");
            size = capacity;
            _inventory = new Inventory(size);
        }

        public bool TryAdd(IItemStack stack, out int remainder) =>
            Value.TryAdd(stack, out remainder);
        public bool TryAdd(ItemData item, int count, out int remainder,
            ItemData.Rarity rarity = ItemData.Rarity.Common,
            byte durability = byte.MaxValue) =>
            Value.TryAdd(item, count, out remainder, rarity, durability);
        public bool TryRemove(IItemStack stack) => Value.TryRemove(stack);
        public bool TryRemove(ItemData item, int count,
            ItemData.Rarity rarity = ItemData.Rarity.Common) =>
            Value.TryRemove(item, count, rarity);
        public bool TryRemove(EntityTag tag, int count) => Value.TryRemove(tag, count);
        public bool TryRemoveOne(EntityTag tag, out ItemData item,
            out ItemData.Rarity rarity) => Value.TryRemoveOne(tag, out item, out rarity);
        public int GetCount(ItemData item,
            ItemData.Rarity rarity = ItemData.Rarity.Common) => Value.GetCount(item, rarity);
        public int GetCount(EntityTag tag) => Value.GetCount(tag);
        public bool Contains(IItemStack stack) => Value.Contains(stack);
        public bool Contains(ItemData item, int count = 1,
            ItemData.Rarity rarity = ItemData.Rarity.Common) => Value.Contains(item, count, rarity);
        public bool Contains(EntityTag tag, int count = 1,
            ItemData.Rarity rarity = ItemData.Rarity.Common) => Value.Contains(tag, count, rarity);
        public bool CanApplyChanges(IReadOnlyList<InventoryChange> changes) =>
            Value.CanApplyChanges(changes);
        public bool TryApplyChanges(IReadOnlyList<InventoryChange> changes) =>
            Value.TryApplyChanges(changes);
        public void Clear() => Value.Clear();

        public void WriteState(BinaryWriter writer)
        {
            writer.Write(Value.OccupiedSlots);
            foreach (IItemStack stack in Value.Stacks)
            {
                writer.Write(stack.Item.persistentId);
                writer.Write((byte)stack.Rarity);
                writer.Write(stack.Count);
                ItemStackDataCodec.Write(writer, stack.Durability, stack.GeneratedData);
            }
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            if (savedVersion < 1 || savedVersion > Version)
                throw new InvalidDataException($"Unsupported town stockpile version {savedVersion}.");
            if (_catalog == null)
                throw new InvalidOperationException("Town stockpile requires an item catalog.");
            int count = reader.ReadInt32();
            if (count < 0 || count > size)
                throw new InvalidDataException("Invalid town stockpile stack count.");
            Inventory restored = new(size);
            for (int i = 0; i < count; i++)
            {
                string id = reader.ReadString();
                ItemData.Rarity rarity = (ItemData.Rarity)reader.ReadByte();
                int amount = reader.ReadInt32();
                byte durability = byte.MaxValue;
                GeneratedItemData generated = savedVersion >= 2
                    ? ItemStackDataCodec.Read(reader, out durability) : null;
                if (savedVersion < 2) durability = reader.ReadByte();
                if (!_catalog.TryGet(id, out ItemData item) || amount < 1 ||
                    !restored.TryAdd(new ItemStack(item, amount, rarity, durability, generated), out int remainder) ||
                    remainder != 0)
                    throw new InvalidDataException($"Invalid stockpile item '{id}'.");
            }
            _inventory = restored;
        }

        public bool IsAtBaseline() => _inventory == null || _inventory.OccupiedSlots == 0;
    }
}
