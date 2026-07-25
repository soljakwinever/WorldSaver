using System;
using System.Collections.Generic;
using System.IO;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class FurnaceComponent : MonoBehaviour, IInteractable,
        IEntityComponent, IPersistentComponent, IOfflineSimulatable,
        IFurnaceStation
    {
        public const ushort TypeId = 11;
        private const ushort CurrentVersion = 1;
        public const int OutputSlotCount = 9;

        [SerializeField] private string windowTitle = "Furnace";
        [SerializeField] private string interactionPrompt = "Use furnace";
        [SerializeField] private EntityTag fuelTag;
        [SerializeField] private ItemData outputItem;
        [SerializeField, Min(1)] private int itemsPerCycle = 1;
        [SerializeField, Min(1)] private long ticksPerCycle = 600;
        [SerializeField, Min(1)] private int fuelValuePerCycle = 1;
        [SerializeField] private ItemData.Rarity outputRarity = ItemData.Rarity.Common;

        private Inventory _fuel = new(1);
        private Inventory _output = new(OutputSlotCount);
        private ItemCatalog _catalog;
        private IWorldClock _clock;
        private IComponentWindowService _window;
        private long _lastTick;
        private long _progressTicks;
        private long _storedFuelUnits;
        private bool _tickInitialized;

        public IPersistentEntity PersistentEntity { get; set; }
        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => CurrentVersion;
        public string WindowTitle => windowTitle;
        public IInventory FuelInventory => _fuel;
        public IInventory OutputInventory => _output;
        public float Progress01 => ticksPerCycle <= 0
            ? 0f
            : Mathf.Clamp01(_progressTicks / (float)ticksPerCycle);
        public bool IsBurning => GetAvailableFuelUnits() >= RequiredFuelUnits;
        private long RequiredFuelUnits => checked(
            (long)fuelValuePerCycle * ItemData.FuelUnitsPerBaseValue);

        [Inject]
        public void Construct(
            IWorldClock clock,
            IComponentWindowService window,
            ItemCatalog catalog)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public void Initialize(
            string title,
            string prompt,
            EntityTag acceptedFuelTag,
            ItemData producedItem,
            int producedPerCycle,
            long cycleTicks,
            int fuelCost,
            ItemData.Rarity rarity = ItemData.Rarity.Common)
        {
            windowTitle = string.IsNullOrWhiteSpace(title) ? "Furnace" : title;
            interactionPrompt = string.IsNullOrWhiteSpace(prompt)
                ? "Use furnace"
                : prompt;
            fuelTag = acceptedFuelTag;
            outputItem = producedItem;
            itemsPerCycle = Math.Max(1, producedPerCycle);
            ticksPerCycle = Math.Max(1, cycleTicks);
            fuelValuePerCycle = Math.Max(1, fuelCost);
            outputRarity = rarity;
            ValidateConfiguration();
        }

        private void Update()
        {
            if (_clock == null)
                return;

            long now = _clock.CurrentTick;
            if (!_tickInitialized)
            {
                _lastTick = now;
                _tickInitialized = true;
                return;
            }

            Simulate(_lastTick, now);
        }

        public void SimulateOffline(
            long fromTick,
            long toTick,
            OfflineSimulationPolicy policy)
        {
            if (policy == OfflineSimulationPolicy.None || toTick <= fromTick)
                return;

            long start = _tickInitialized ? _lastTick : fromTick;
            _tickInitialized = true;
            Simulate(start, toTick);
        }

        private void Simulate(long fromTick, long toTick)
        {
            if (toTick <= fromTick)
                return;

            ValidateConfiguration();
            long elapsed = toTick - fromTick;
            long progressBeforeSimulation = _progressTicks;
            long totalProgress = checked(_progressTicks + elapsed);
            long requestedCycles = totalProgress / ticksPerCycle;
            if (requestedCycles == 0)
            {
                _progressTicks = totalProgress;
                _lastTick = toTick;
                return;
            }

            long fuelCycles = GetAvailableFuelUnits() / RequiredFuelUnits;
            long cycles = Math.Min(requestedCycles, fuelCycles);
            cycles = Math.Min(cycles, GetOutputCycleCapacity());
            if (cycles > 0)
            {
                ConsumeFuel(checked(cycles * RequiredFuelUnits));
                int amount = checked((int)cycles * itemsPerCycle);
                if (!_output.TryAdd(outputItem, amount, out int remainder,
                        outputRarity) || remainder != 0)
                {
                    throw new InvalidOperationException(
                        "Furnace output capacity changed during simulation.");
                }
            }

            _progressTicks = cycles == requestedCycles
                ? totalProgress % ticksPerCycle
                : cycles == 0
                    ? progressBeforeSimulation
                    : 0;
            _lastTick = toTick;
        }

        public bool TryInsertFuel(IInventory source, IItemStack stack)
        {
            if (source == null || stack == null || !IsValidFuel(stack.Item))
                return false;
            if (!_fuel.CanApplyChanges(new[]
                {
                    new InventoryChange(stack.Item, stack.Count, stack.Rarity)
                }))
                return false;
            if (!source.TryRemove(stack))
                return false;
            if (_fuel.TryAdd(stack, out int remainder) && remainder == 0)
                return true;

            source.TryAdd(stack, out _);
            return false;
        }

        public bool TryCollectOutput(IInventory destination, IItemStack stack)
        {
            if (destination == null || stack == null ||
                !_output.Contains(stack))
                return false;
            var addition = new[]
            {
                new InventoryChange(stack.Item, stack.Count, stack.Rarity)
            };
            if (!destination.CanApplyChanges(addition) ||
                !destination.TryApplyChanges(addition))
                return false;
            if (_output.TryRemove(stack))
                return true;

            destination.TryApplyChanges(new[]
            {
                new InventoryChange(stack.Item, -stack.Count, stack.Rarity)
            });
            return false;
        }

        public int CollectAll(IInventory destination)
        {
            int collected = 0;
            while (_output.Stacks.Count > 0)
            {
                IItemStack stack = _output.Stacks[0];
                int count = stack.Count;
                if (!TryCollectOutput(destination, stack))
                    break;
                collected = checked(collected + count);
            }
            return collected;
        }

        public bool CanInteract(InteractionContext context) =>
            context.interactionType == InteractionType.Direct &&
            _window != null &&
            context.user != null &&
            context.user.GetComponentInParent<PersistentInventory>() != null;

        public void Interact(InteractionContext context)
        {
            if (!CanInteract(context))
                return;
            IInventory player =
                context.user.GetComponentInParent<PersistentInventory>();
            _window.Open(new ComponentWindowRequest(
                windowTitle,
                new Vector2(560f, 430f),
                new FurnaceWindowSection(this, player)));
        }

        public string GetInteractionPrompt(InteractionContext context) =>
            context.interactionType == InteractionType.Direct
                ? interactionPrompt
                : string.Empty;

        public Vector3 GetPosition() => transform.position;

        public void WriteState(BinaryWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            writer.Write(_lastTick);
            writer.Write(_progressTicks);
            writer.Write(_storedFuelUnits);
            WriteInventory(writer, _fuel);
            WriteInventory(writer, _output);
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            if (savedVersion != CurrentVersion)
                throw new InvalidDataException(
                    $"Unsupported furnace state version {savedVersion}.");

            _lastTick = reader.ReadInt64();
            _progressTicks = reader.ReadInt64();
            _storedFuelUnits = reader.ReadInt64();
            if (_progressTicks < 0 || _progressTicks >= ticksPerCycle ||
                _storedFuelUnits < 0)
                throw new InvalidDataException("Furnace timing or fuel state is invalid.");
            _fuel = ReadInventory(reader, 1, true);
            _output = ReadInventory(reader, OutputSlotCount, false);
            _tickInitialized = _lastTick != 0;
        }

        public bool IsAtBaseline() =>
            _lastTick == 0 && _progressTicks == 0 && _storedFuelUnits == 0 &&
            _fuel.OccupiedSlots == 0 && _output.OccupiedSlots == 0;

        private long GetAvailableFuelUnits()
        {
            long units = _storedFuelUnits;
            foreach (IItemStack stack in _fuel.Stacks)
                units = checked(units + checked(
                    stack.Count * stack.Item.GetFuelUnits(stack.Rarity)));
            return units;
        }

        private void ConsumeFuel(long units)
        {
            while (_storedFuelUnits < units)
            {
                IItemStack stack = _fuel.Stacks[0];
                long value = stack.Item.GetFuelUnits(stack.Rarity);
                _fuel.TryRemove(new ItemStack(stack.Item, 1, stack.Rarity));
                _storedFuelUnits = checked(_storedFuelUnits + value);
            }
            _storedFuelUnits -= units;
        }

        private long GetOutputCycleCapacity()
        {
            int capacity = 0;
            foreach (IItemStack stack in _output.Stacks)
            {
                if (stack.Item == outputItem && stack.Rarity == outputRarity)
                    capacity = checked(capacity + stack.RemainingCapacity);
            }
            capacity = checked(capacity +
                (OutputSlotCount - _output.OccupiedSlots) * outputItem.maxStack);
            return capacity / itemsPerCycle;
        }

        private bool IsValidFuel(ItemData item) =>
            item != null && fuelTag != null && item.HasTag(fuelTag) &&
            item.fuelValue > 0;

        private void WriteInventory(BinaryWriter writer, IInventory inventory)
        {
            writer.Write(inventory.Stacks.Count);
            foreach (IItemStack stack in inventory.Stacks)
            {
                writer.Write(stack.Item.persistentId);
                writer.Write((byte)stack.Rarity);
                writer.Write(stack.Count);
            }
        }

        private Inventory ReadInventory(
            BinaryReader reader,
            int size,
            bool requireFuel)
        {
            if (_catalog == null)
                throw new InvalidOperationException(
                    "Furnace must be constructed before loading state.");
            int count = reader.ReadInt32();
            if (count < 0 || count > size)
                throw new InvalidDataException("Furnace inventory size is invalid.");
            Inventory inventory = new(size);
            for (int i = 0; i < count; i++)
            {
                string id = reader.ReadString();
                ItemData.Rarity rarity = (ItemData.Rarity)reader.ReadByte();
                int amount = reader.ReadInt32();
                if (!_catalog.TryGet(id, out ItemData item) ||
                    !Enum.IsDefined(typeof(ItemData.Rarity), rarity) ||
                    amount <= 0 || amount > item.maxStack ||
                    (requireFuel && !IsValidFuel(item)))
                    throw new InvalidDataException(
                        $"Invalid furnace item stack '{id}'.");
                inventory.TryAdd(item, amount, out int remainder, rarity);
                if (remainder != 0)
                    throw new InvalidDataException("Furnace inventory exceeds capacity.");
            }
            return inventory;
        }

        private void ValidateConfiguration()
        {
            if (fuelTag == null || outputItem == null)
                throw new InvalidOperationException(
                    "Furnace requires a fuel tag and output item.");
            if (itemsPerCycle < 1 || ticksPerCycle < 1 ||
                fuelValuePerCycle < 1)
                throw new InvalidOperationException(
                    "Furnace cycle values must be positive.");
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            itemsPerCycle = Mathf.Max(1, itemsPerCycle);
            ticksPerCycle = Math.Max(1, ticksPerCycle);
            fuelValuePerCycle = Mathf.Max(1, fuelValuePerCycle);
        }
#endif
    }
}
