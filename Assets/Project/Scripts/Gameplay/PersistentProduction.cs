using System;
using System.IO;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using Project.Scripts.Utility;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    [RequireComponent(typeof(PersistentInventory))]
    public sealed class PersistentProduction : MonoBehaviour, IPersistentComponent, IOfflineSimulatable
    {
        public const ushort TypeId = 6;
        private const ushort CurrentVersion = 2;
        private const int RarityCount = (int)ItemData.Rarity.Legendary + 1;

        [SerializeField] private ItemData outputItem;
        [SerializeField, Min(1)] private int itemsPerCycle = 1;
        [SerializeField, Min(1)] private long ticksPerCycle = 600;
        [SerializeField] private bool generateRarity;
        [SerializeField] private ItemData.Rarity rarity = ItemData.Rarity.Common;

        private readonly int[] _pendingByRarity = new int[RarityCount];
        private PersistentInventory _inventory;
        private long _lastProductionTick;

        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => CurrentVersion;

        public void Initialize(
            ItemData item,
            int outputPerCycle,
            long cycleTicks,
            bool shouldGenerateRarity,
            ItemData.Rarity fixedRarity = ItemData.Rarity.Common)
        {
            outputItem = item != null
                ? item
                : throw new ArgumentNullException(nameof(item));
            itemsPerCycle = Math.Max(1, outputPerCycle);
            ticksPerCycle = Math.Max(1, cycleTicks);
            generateRarity = shouldGenerateRarity;
            rarity = fixedRarity;
            ValidateConfiguration();
            ResolveInventory();
        }

        public void SimulateOffline(
            long fromTick,
            long toTick,
            OfflineSimulationPolicy policy)
        {
            if (policy == OfflineSimulationPolicy.None || toTick <= fromTick)
                return;

            ValidateConfiguration();
            ResolveInventory();
            FlushPendingOutput();

            long productionStart = _lastProductionTick == 0
                ? fromTick
                : Math.Max(fromTick, _lastProductionTick);
            long cycles = (toTick - productionStart) / ticksPerCycle;
            if (cycles <= 0)
                return;

            long produced = checked(cycles * itemsPerCycle);
            AddPendingOutput(produced);
            _lastProductionTick = checked(productionStart + cycles * ticksPerCycle);
            FlushPendingOutput();
        }

        public void WriteState(BinaryWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            writer.Write(_lastProductionTick);
            for (int i = 0; i < RarityCount; i++)
                writer.Write(_pendingByRarity[i]);
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            if (savedVersion == 0 || savedVersion > CurrentVersion)
                throw new InvalidDataException(
                    $"Unsupported production state version {savedVersion}.");

            _lastProductionTick = reader.ReadInt64();
            Array.Clear(_pendingByRarity, 0, _pendingByRarity.Length);

            if (savedVersion == 1)
            {
                _pendingByRarity[(int)ItemData.Rarity.Common] = reader.ReadInt32();
                return;
            }

            for (int i = 0; i < RarityCount; i++)
            {
                int pending = reader.ReadInt32();
                if (pending < 0)
                    throw new InvalidDataException("Production has a negative pending output.");
                _pendingByRarity[i] = pending;
            }
        }

        public bool IsAtBaseline()
        {
            if (_lastProductionTick != 0)
                return false;

            for (int i = 0; i < RarityCount; i++)
            {
                if (_pendingByRarity[i] != 0)
                    return false;
            }

            return true;
        }

        private void AddPendingOutput(long count)
        {
            if (!generateRarity)
            {
                AddPending(rarity, count);
                return;
            }

            for (long i = 0; i < count; i++)
                AddPending(ItemRarityUtility.Generate(), 1);
        }

        private void AddPending(ItemData.Rarity outputRarity, long count)
        {
            int index = (int)outputRarity;
            _pendingByRarity[index] = checked(
                _pendingByRarity[index] + checked((int)count));
        }

        private void FlushPendingOutput()
        {
            for (int i = 0; i < RarityCount; i++)
            {
                int pending = _pendingByRarity[i];
                if (pending == 0)
                    continue;

                _inventory.TryAdd(
                    outputItem,
                    pending,
                    out int remainder,
                    (ItemData.Rarity)i);
                _pendingByRarity[i] = remainder;
            }
        }

        private void ResolveInventory()
        {
            if (_inventory == null)
                _inventory = GetComponent<PersistentInventory>();
            if (_inventory == null)
                throw new InvalidOperationException(
                    "PersistentProduction requires a PersistentInventory.");
        }

        private void ValidateConfiguration()
        {
            if (outputItem == null)
                throw new InvalidOperationException("Production requires an output item.");
            if (itemsPerCycle < 1 || ticksPerCycle < 1)
                throw new InvalidOperationException(
                    "Production amounts and cycle duration must be positive.");
            if (!Enum.IsDefined(typeof(ItemData.Rarity), rarity))
                throw new InvalidOperationException("Production has an invalid fixed rarity.");
        }

        private void OnValidate()
        {
            itemsPerCycle = Mathf.Max(1, itemsPerCycle);
            ticksPerCycle = Math.Max(1, ticksPerCycle);
        }
    }
}
