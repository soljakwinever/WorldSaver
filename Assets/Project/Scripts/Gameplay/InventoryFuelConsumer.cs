using System;
using System.IO;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;
using UnityEngine.Serialization;

namespace Project.Scripts.Gameplay
{
    public sealed class InventoryFuelConsumer : MonoBehaviour, IOperationCondition,
        IPersistentComponent
    {
        public const ushort TypeId = 8;
        private const ushort CurrentVersion = 2;

        [SerializeField] private EntityTag fuelTag;
        [FormerlySerializedAs("itemsPerOperation")]
        [SerializeField, Min(1)] private int fuelValuePerOperation = 1;

        private PersistentInventory _inventory;
        private long _storedFuelUnits;

        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => CurrentVersion;

        public bool ConsumesOperations => true;

        public long AvailableOperations
        {
            get
            {
                ResolveInventory();
                ValidateConfiguration();
                long availableUnits = checked(
                    _storedFuelUnits + GetInventoryFuelUnits());
                return availableUnits / GetRequiredUnitsPerOperation();
            }
        }

        public bool HasFuel => AvailableOperations > 0;

        public double StoredFuelValue =>
            _storedFuelUnits / (double)ItemData.FuelUnitsPerBaseValue;

        public void Initialize(EntityTag tag, int valueRequiredPerOperation)
        {
            fuelTag = tag != null ? tag : throw new ArgumentNullException(nameof(tag));
            fuelValuePerOperation = Math.Max(1, valueRequiredPerOperation);
            ResolveInventory();
        }

        public void Commit(long operations)
        {
            if (operations < 0)
                throw new ArgumentOutOfRangeException(nameof(operations));
            if (operations == 0)
                return;

            ResolveInventory();
            ValidateConfiguration();
            long requiredUnits = checked(
                operations * GetRequiredUnitsPerOperation());
            if (requiredUnits > checked(_storedFuelUnits + GetInventoryFuelUnits()))
                throw new InvalidOperationException("There is not enough fuel value.");

            while (_storedFuelUnits < requiredUnits)
            {
                if (!_inventory.TryRemoveOne(
                        fuelTag,
                        out ItemData item,
                        out ItemData.Rarity rarity))
                    throw new InvalidOperationException(
                        "Fuel availability changed before it could be committed.");
                ValidateFuelItem(item);
                _storedFuelUnits = checked(
                    _storedFuelUnits + item.GetFuelUnits(rarity));
            }

            _storedFuelUnits -= requiredUnits;
        }

        public void WriteState(BinaryWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            writer.Write(_storedFuelUnits);
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            if (savedVersion == 0 || savedVersion > CurrentVersion)
                throw new InvalidDataException(
                    $"Unsupported fuel consumer state version {savedVersion}.");

            long storedUnits = reader.ReadInt64();
            if (storedUnits < 0)
                throw new InvalidDataException(
                    "Fuel consumer has a negative stored value.");
            _storedFuelUnits = savedVersion == 1
                ? checked(storedUnits * ItemData.FuelUnitsPerBaseValue)
                : storedUnits;
        }

        public bool IsAtBaseline()
        {
            return _storedFuelUnits == 0;
        }

        private long GetInventoryFuelUnits()
        {
            long total = 0;
            var stacks = _inventory.Stacks;
            for (int i = 0; i < stacks.Count; i++)
            {
                IItemStack stack = stacks[i];
                if (!stack.Item.HasTag(fuelTag))
                    continue;

                ValidateFuelItem(stack.Item);
                total = checked(total + checked(
                    stack.Count * stack.Item.GetFuelUnits(stack.Rarity)));
            }

            return total;
        }

        private long GetRequiredUnitsPerOperation()
        {
            return checked(
                (long)fuelValuePerOperation * ItemData.FuelUnitsPerBaseValue);
        }

        private void ResolveInventory()
        {
            if (_inventory == null)
                _inventory = GetComponent<PersistentInventory>();
            if (_inventory == null)
                throw new InvalidOperationException(
                    "InventoryFuelConsumer requires a PersistentInventory.");
        }

        private void ValidateConfiguration()
        {
            if (fuelTag == null)
                throw new InvalidOperationException("A fuel item tag is required.");
            if (fuelValuePerOperation < 1)
                throw new InvalidOperationException(
                    "Fuel value per operation must be positive.");
        }

        private static void ValidateFuelItem(ItemData item)
        {
            if (item.GetFuelUnits(ItemData.Rarity.Common) < 1)
                throw new InvalidOperationException(
                    $"Fuel item '{item.name}' must have a positive fuel value.");
        }

        private void OnValidate()
        {
            fuelValuePerOperation = Mathf.Max(1, fuelValuePerOperation);
        }
    }
}
