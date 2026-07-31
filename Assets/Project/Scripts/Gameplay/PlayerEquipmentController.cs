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
    [RequireComponent(typeof(PersistentInventory))]
    public sealed class PlayerEquipmentController :
        MonoBehaviour,
        IPersistentComponent
    {
        public const ushort TypeId = 15;
        private const ushort CurrentVersion = 1;

        private readonly Dictionary<EquipmentSlot, ItemStack> _equipped =
            new();
        private readonly List<IItemStack> _equippedView = new();

        private PersistentInventory _inventory;
        private PlayerDataController _player;
        private ItemCatalog _catalog;

        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => CurrentVersion;
        public IReadOnlyList<IItemStack> EquippedItems =>
            _equippedView;

        [Inject]
        public void Construct(ItemCatalog catalog)
        {
            _catalog = catalog ??
                throw new ArgumentNullException(nameof(catalog));
        }

        private void Awake()
        {
            _inventory = GetComponent<PersistentInventory>();
            _player = GetComponent<PlayerDataController>();
        }

        public bool TryGetEquipped(
            EquipmentSlot slot,
            out IItemStack stack)
        {
            bool found = _equipped.TryGetValue(slot, out ItemStack item);
            stack = item;
            return found;
        }

        public bool IsEquipped(ItemData item)
        {
            if (item == null)
                return false;

            foreach (ItemStack stack in _equipped.Values)
            {
                if (stack.Item == item)
                    return true;
            }

            return false;
        }

        public int GetStatModifier(EquipmentStat stat)
        {
            int total = 0;
            foreach (ItemStack stack in _equipped.Values)
            {
                if (stack.Item is EquipableItemData equipable &&
                    stack.Durability > 0)
                {
                    total = checked(
                        total + equipable.GetStatModifier(stat));
                }
            }

            return total;
        }

        public bool TryEquip(IItemStack sourceStack)
        {
            EnsureComponents();
            if (sourceStack == null ||
                sourceStack.Count < 1 ||
                sourceStack.Item is not EquipableItemData equipable ||
                !_inventory.Contains(new ItemStack(
                    sourceStack.Item,
                    1,
                    sourceStack.Rarity,
                    sourceStack.Durability)))
                return false;

            var changes = new List<InventoryChange>
            {
                new(
                    sourceStack.Item,
                    -1,
                    sourceStack.Rarity,
                    sourceStack.Durability)
            };

            if (_equipped.TryGetValue(
                    equipable.EquipmentSlot,
                    out ItemStack previous))
            {
                changes.Add(new InventoryChange(
                    previous.Item,
                    1,
                    previous.Rarity,
                    previous.Durability));
            }

            if (!_inventory.TryApplyChanges(changes))
                return false;

            _equipped[equipable.EquipmentSlot] = new ItemStack(
                sourceStack.Item,
                1,
                sourceStack.Rarity,
                sourceStack.Durability);
            RebuildView();
            NotifyChanged();
            return true;
        }

        public bool TryUnequip(EquipmentSlot slot)
        {
            EnsureComponents();
            if (!_equipped.TryGetValue(slot, out ItemStack stack))
                return false;

            var addition = new[]
            {
                new InventoryChange(
                    stack.Item,
                    1,
                    stack.Rarity,
                    stack.Durability)
            };
            if (!_inventory.TryApplyChanges(addition))
                return false;

            _equipped.Remove(slot);
            RebuildView();
            NotifyChanged();
            return true;
        }

        public bool TryDamageDurability(
            EquipmentSlot slot,
            byte amount)
        {
            if (amount == 0 ||
                !_equipped.TryGetValue(slot, out ItemStack stack))
                return false;

            stack.ApplyDurabilityDamage(amount);
            NotifyChanged();
            return true;
        }

        public void WriteState(BinaryWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            writer.Write(_equipped.Count);
            foreach (EquipmentSlot slot in
                     Enum.GetValues(typeof(EquipmentSlot)))
            {
                if (!_equipped.TryGetValue(slot, out ItemStack stack))
                    continue;
                if (string.IsNullOrWhiteSpace(stack.Item.persistentId))
                    throw new InvalidOperationException(
                        "Equipped items require persistent IDs.");

                writer.Write((byte)slot);
                writer.Write(stack.Item.persistentId);
                writer.Write((byte)stack.Rarity);
                writer.Write(stack.Durability);
            }
        }

        public void ReadState(
            BinaryReader reader,
            ushort savedVersion)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            if (savedVersion != CurrentVersion)
                throw new InvalidDataException(
                    $"Unsupported equipment state version {savedVersion}.");
            _catalog ??= FindFirstObjectByType<ItemCatalog>();
            if (_catalog == null)
                throw new InvalidOperationException(
                    "The item catalog must be assigned before equipment loads.");

            int count = reader.ReadInt32();
            int maximumSlots =
                Enum.GetValues(typeof(EquipmentSlot)).Length;
            if (count < 0 || count > maximumSlots)
                throw new InvalidDataException(
                    $"Invalid equipped item count {count}.");

            var restored =
                new Dictionary<EquipmentSlot, ItemStack>();
            for (int i = 0; i < count; i++)
            {
                EquipmentSlot slot =
                    (EquipmentSlot)reader.ReadByte();
                string itemId = reader.ReadString();
                ItemData.Rarity rarity =
                    (ItemData.Rarity)reader.ReadByte();
                byte durability = reader.ReadByte();

                if (!Enum.IsDefined(typeof(EquipmentSlot), slot) ||
                    !Enum.IsDefined(typeof(ItemData.Rarity), rarity) ||
                    !_catalog.TryGet(itemId, out ItemData item) ||
                    item is not EquipableItemData equipable ||
                    equipable.EquipmentSlot != slot ||
                    !restored.TryAdd(
                        slot,
                        new ItemStack(
                            item, 1, rarity, durability)))
                {
                    throw new InvalidDataException(
                        $"Invalid equipped item '{itemId}'.");
                }
            }

            _equipped.Clear();
            foreach (KeyValuePair<EquipmentSlot, ItemStack> pair in
                     restored)
                _equipped.Add(pair.Key, pair.Value);
            RebuildView();
            NotifyChanged();
        }

        public bool IsAtBaseline() => _equipped.Count == 0;

        private void EnsureComponents()
        {
            _inventory ??= GetComponent<PersistentInventory>();
            _player ??= GetComponent<PlayerDataController>();
            if (_inventory == null)
                throw new InvalidOperationException(
                    "Player equipment requires a persistent inventory.");
        }

        private void RebuildView()
        {
            _equippedView.Clear();
            foreach (EquipmentSlot slot in
                     Enum.GetValues(typeof(EquipmentSlot)))
            {
                if (_equipped.TryGetValue(slot, out ItemStack stack))
                    _equippedView.Add(stack);
            }
        }

        private void NotifyChanged()
        {
            EnsureComponents();
            _player?.NotifyEquipmentChanged();
        }
    }
}
