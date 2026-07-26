using System;
using System.Collections.Generic;
using System.IO;
using Project.Scripts.Bus;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    public class PlayerToolbarController : MonoBehaviour, IPersistentComponent
    {
        public const ushort TypeId = 12;
        private const ushort CurrentVersion = 1;
        public const int SlotCount = 10;

        //Injected components
        private IInputManager _inputManager;
        private PlayerBus _playerBus;
        private ItemCatalog _itemCatalog;
        
        private readonly IHotbarAction[] _hotbarActions = new IHotbarAction[SlotCount];
        private int _hotbarIndex = 0;
        public IHotbarAction SelectedItemAction => _hotbarActions[_hotbarIndex];
        public IReadOnlyList<IHotbarAction> HotbarActions => _hotbarActions;
        public int SelectedIndex => _hotbarIndex;
        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => CurrentVersion;
        
        [Inject]
        public void Constract(
            IInputManager inputManager,
            PlayerBus playerBus,
            ItemCatalog itemCatalog)
        {
            _inputManager = inputManager;
            _inputManager.InputPerformed += InputManagerOnInputPerformed;
            
            _playerBus = playerBus;
            _itemCatalog = itemCatalog;
        }

        public void SetHotbarAction(int hotbarIndex, IHotbarAction action)
        {
            ValidateIndex(hotbarIndex);
            _hotbarActions[hotbarIndex] = action;
            _playerBus?.RaiseHotbarActionSet(hotbarIndex, action);
        }

        public void WriteState(BinaryWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            writer.Write(_hotbarIndex);
            writer.Write(_hotbarActions.Length);
            foreach (IHotbarAction action in _hotbarActions)
            {
                writer.Write(action != null);
                if (action == null)
                    continue;

                if (action is not ItemActionBinding binding ||
                    binding.ItemData == null)
                    throw new InvalidOperationException(
                        "Only item-backed toolbar actions can be persisted.");

                if (string.IsNullOrWhiteSpace(binding.ItemData.persistentId))
                    throw new InvalidOperationException(
                        "A toolbar item requires a non-empty persistent ID.");
                writer.Write(binding.ItemData.persistentId);
            }
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            if (savedVersion != CurrentVersion)
                throw new InvalidDataException(
                    $"Unsupported toolbar state version {savedVersion}.");
            if (_itemCatalog == null)
                throw new InvalidOperationException(
                    "The item catalog must be assigned before toolbar state is loaded.");

            int selectedIndex = reader.ReadInt32();
            int slotCount = reader.ReadInt32();
            if (selectedIndex < 0 || selectedIndex >= SlotCount)
                throw new InvalidDataException(
                    $"Saved toolbar index {selectedIndex} is invalid.");
            if (slotCount != SlotCount)
                throw new InvalidDataException(
                    $"Saved toolbar has {slotCount} slots; expected {SlotCount}.");

            IHotbarAction[] restoredActions = new IHotbarAction[SlotCount];
            for (int i = 0; i < restoredActions.Length; i++)
            {
                if (reader.ReadBoolean())
                {
                    string itemId = reader.ReadString();
                    if (!_itemCatalog.TryGet(itemId, out ItemData item))
                        throw new InvalidDataException(
                            $"Saved toolbar references unknown item '{itemId}'.");
                    if (item.action == null)
                        throw new InvalidDataException(
                            $"Saved toolbar item '{itemId}' has no action.");
                    restoredActions[i] = new ItemActionBinding(item);
                }
            }

            _hotbarIndex = selectedIndex;
            for (int i = 0; i < restoredActions.Length; i++)
                SetHotbarAction(i, restoredActions[i]);
            _playerBus?.RaiseHotbarIndexChanged(_hotbarIndex, _hotbarActions);
        }

        public bool IsAtBaseline()
        {
            if (_hotbarIndex != 0)
                return false;

            foreach (IHotbarAction action in _hotbarActions)
            {
                if (action != null)
                    return false;
            }

            return true;
        }

        private void InputManagerOnInputPerformed(InputContext obj)
        {
            if (obj.HotBarPressed != InputContext.NoHotbarKeyPressed)
            {
                HandleHotbar(obj.HotBarPressed);
            }
        }

        private void HandleHotbar(int objHotBarPressed)
        {
            ValidateIndex(objHotBarPressed);
            _hotbarIndex = objHotBarPressed;
            _playerBus?.RaiseHotbarIndexChanged(_hotbarIndex, _hotbarActions);
        }

        private static void ValidateIndex(int index)
        {
            if (index < 0 || index >= SlotCount)
                throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}
