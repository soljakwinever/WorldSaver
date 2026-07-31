using System;
using System.Collections.Generic;
using System.IO;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    public sealed class PersistentInventory : MonoBehaviour, IInventory, IPersistentComponent
    {
        public const ushort TypeId = 7;
        private const ushort CurrentVersion = 2;

        [SerializeField, Min(1)] private int size = 16;

        private Inventory _inventory;
        private Dictionary<string, ItemData> _itemsById;
        private ItemCatalog _itemCatalog;

        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => CurrentVersion;

        public int Size => GetInventory().Size;
        public int OccupiedSlots => GetInventory().OccupiedSlots;
        public IReadOnlyList<IItemStack> Stacks => GetInventory().Stacks;

        private void Awake()
        {
            EnsureInitialized();
        }

        [Inject]
        public void Construct(ItemCatalog itemCatalog)
        {
            if (itemCatalog == null)
                throw new ArgumentNullException(nameof(itemCatalog));
            if (_inventory != null && _inventory.OccupiedSlots > 0)
                throw new InvalidOperationException("A non-empty inventory cannot change its item catalog.");

            _itemCatalog = itemCatalog;
            _itemsById = null;
            _inventory = new Inventory(size);
        }

        public void Configure(int inventorySize, IReadOnlyList<ItemData> catalog)
        {
            if (_inventory != null && _inventory.OccupiedSlots > 0)
                throw new InvalidOperationException("A non-empty inventory cannot be reconfigured.");
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            ItemData[] configuredCatalog = new ItemData[catalog.Count];
            for (int i = 0; i < catalog.Count; i++)
                configuredCatalog[i] = catalog[i];

            Dictionary<string, ItemData> itemsById = BuildCatalog(configuredCatalog);
            Inventory inventory = new(inventorySize);

            size = inventorySize;
            _itemCatalog = null;
            _itemsById = itemsById;
            _inventory = inventory;
        }

        public bool TryAdd(ItemData item, int count, out int remainder,
            ItemData.Rarity rarity = ItemData.Rarity.Common,
            byte durability = byte.MaxValue)
        {
            EnsureCatalogContains(item);
            return GetInventory().TryAdd(
                item, count, out remainder, rarity, durability);
        }

        public bool TryAdd(IItemStack stack, out int remainder)
        {
            ValidateStack(stack);
            EnsureCatalogContains(stack.Item);
            return GetInventory().TryAdd(stack, out remainder);
        }

        public bool TryRemove(ItemData item, int count,
            ItemData.Rarity rarity = ItemData.Rarity.Common)
        {
            return GetInventory().TryRemove(item, count, rarity);
        }

        public bool TryRemove(IItemStack stack)
        {
            ValidateStack(stack);
            return GetInventory().TryRemove(stack);
        }

        public bool TryRemove(EntityTag tag, int count)
        {
            return GetInventory().TryRemove(tag, count);
        }

        public bool TryRemoveOne(
            EntityTag tag,
            out ItemData item,
            out ItemData.Rarity rarity)
        {
            return GetInventory().TryRemoveOne(tag, out item, out rarity);
        }

        public int GetCount(ItemData item, ItemData.Rarity rarity = ItemData.Rarity.Common)
        {
            return GetInventory().GetCount(item, rarity);
        }

        public int GetCount(EntityTag tag)
        {
            return GetInventory().GetCount(tag);
        }

        public bool Contains(ItemData item, int count = 1,
            ItemData.Rarity rarity = ItemData.Rarity.Common)
        {
            return GetInventory().Contains(item, count, rarity);
        }

        public bool Contains(EntityTag tag, int count = 1,
            ItemData.Rarity rarity = ItemData.Rarity.Common)
        {
            return GetInventory().Contains(tag, count, rarity);
        }

        public bool Contains(IItemStack stack)
        {
            ValidateStack(stack);
            return GetInventory().Contains(stack);
        }

        public bool CanApplyChanges(IReadOnlyList<InventoryChange> changes)
        {
            ValidateChanges(changes);
            return GetInventory().CanApplyChanges(changes);
        }

        public bool TryApplyChanges(IReadOnlyList<InventoryChange> changes)
        {
            ValidateChanges(changes);
            return GetInventory().TryApplyChanges(changes);
        }

        public void Clear()
        {
            GetInventory().Clear();
        }

        public void WriteState(BinaryWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            EnsureInitialized();
            writer.Write(_inventory.OccupiedSlots);

            foreach (IItemStack stack in _inventory.Stacks)
            {
                ValidatePersistentId(stack.Item);
                writer.Write(stack.Item.persistentId);
                writer.Write((byte)stack.Rarity);
                writer.Write(stack.Count);
                writer.Write(stack.Durability);
            }
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            if (savedVersion < 1 || savedVersion > CurrentVersion)
                throw new InvalidDataException($"Unsupported inventory state version {savedVersion}.");

            EnsureInitialized();
            int stackCount = reader.ReadInt32();
            if (stackCount < 0 || stackCount > size)
                throw new InvalidDataException($"Invalid saved inventory stack count {stackCount}.");

            Inventory restored = new(size);
            for (int i = 0; i < stackCount; i++)
            {
                string itemId = reader.ReadString();
                ItemData.Rarity rarity = (ItemData.Rarity)reader.ReadByte();
                int count = reader.ReadInt32();
                byte durability = savedVersion >= 2
                    ? reader.ReadByte()
                    : byte.MaxValue;

                if (!TryResolveItem(itemId, out ItemData item))
                    throw new InvalidDataException($"Saved inventory references unknown item '{itemId}'.");
                if (!Enum.IsDefined(typeof(ItemData.Rarity), rarity))
                    throw new InvalidDataException($"Saved inventory has invalid rarity {(byte)rarity}.");
                if (count <= 0 || count > item.maxStack)
                    throw new InvalidDataException(
                        $"Saved stack count {count} is invalid for item '{itemId}'.");

                ItemStack stack = new(item, count, rarity, durability);
                if (!restored.TryAdd(stack, out int remainder) || remainder != 0)
                    throw new InvalidDataException("Saved inventory exceeds its configured capacity.");
            }

            _inventory = restored;
        }

        public bool IsAtBaseline()
        {
            return _inventory == null || _inventory.OccupiedSlots == 0;
        }

        private Inventory GetInventory()
        {
            EnsureInitialized();
            return _inventory;
        }

        private void EnsureInitialized()
        {
            if (_inventory != null)
                return;

            Inventory inventory = new(size);
            if (_itemCatalog == null)
                _itemsById ??= new Dictionary<string, ItemData>(StringComparer.Ordinal);
            _inventory = inventory;
        }

        private void EnsureCatalogContains(ItemData item)
        {
            ValidatePersistentId(item);
            EnsureInitialized();

            bool registered = _itemCatalog != null
                ? _itemCatalog.Contains(item)
                : _itemsById.TryGetValue(item.persistentId, out ItemData localItem) && localItem == item;

            if (!registered)
                throw new ArgumentException(
                    $"Item '{item.persistentId}' is not registered in this inventory's catalog.", nameof(item));
        }

        private bool TryResolveItem(string persistentId, out ItemData item)
        {
            EnsureInitialized();
            return _itemCatalog != null
                ? _itemCatalog.TryGet(persistentId, out item)
                : _itemsById.TryGetValue(persistentId, out item);
        }

        private static Dictionary<string, ItemData> BuildCatalog(IReadOnlyList<ItemData> catalog)
        {
            Dictionary<string, ItemData> result = new(StringComparer.Ordinal);
            for (int i = 0; i < catalog.Count; i++)
            {
                ItemData item = catalog[i];
                ValidatePersistentId(item);
                if (!result.TryAdd(item.persistentId, item))
                    throw new InvalidOperationException(
                        $"The item catalog contains duplicate persistent ID '{item.persistentId}'.");
            }

            return result;
        }

        private static void ValidatePersistentId(ItemData item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            if (string.IsNullOrWhiteSpace(item.persistentId))
                throw new InvalidOperationException("Every persistent item requires a non-empty persistentId.");
        }

        private static void ValidateStack(IItemStack stack)
        {
            if (stack == null)
                throw new ArgumentNullException(nameof(stack));
            ValidatePersistentId(stack.Item);
            if (stack.Count <= 0 || stack.Count > stack.Item.maxStack)
                throw new ArgumentException("The item stack has an invalid count.", nameof(stack));
            if (!Enum.IsDefined(typeof(ItemData.Rarity), stack.Rarity))
                throw new ArgumentException("The item stack has an invalid rarity.", nameof(stack));
        }

        public void Initialize(int size)
        {
            if (_inventory != null && _inventory.OccupiedSlots > 0)
                throw new InvalidOperationException("A non-empty inventory cannot be reconfigured.");

            this.size = size;
            _inventory = new Inventory(size);
        }

        private void ValidateChanges(IReadOnlyList<InventoryChange> changes)
        {
            if (changes == null)
                throw new ArgumentNullException(nameof(changes));

            for (int i = 0; i < changes.Count; i++)
                EnsureCatalogContains(changes[i].Item);
        }
    }
}
