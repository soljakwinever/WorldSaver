using System;
using Project.Scripts.Interface;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Zenject;

namespace Project.Scripts.Core
{
    public sealed class InputManager : IInputManager, IInitializable, ITickable, IDisposable
    {
        private Vector2 lastMovement;
        private Inputs _inputs;
        private InputAction _move;
        private InputAction _interact;
        private InputAction _inventory;
        private InputAction _crafting;
        private InputAction _attack;
        private InputAction _skill;
        private InputAction _hotKeyPressed;
        private InputAction _mousePosition;
        
        private bool _ownsSkillAction;
        private int _pendingHotKey = -1;
        
        public Vector2 MousePosition => _mousePosition.ReadValue<Vector2>();
        public bool AttackHeld => _attack?.IsPressed() ?? false;

        public InputContext Context => CreateContext(out _);
        public event Action<InputContext> InputPerformed;

        public void Initialize()
        {
            _inputs = new Inputs();
            string bindingOverrides = PlayerPrefs.GetString(
                "WorldSaver.BindingOverrides",
                string.Empty);
            if (!string.IsNullOrEmpty(bindingOverrides))
                _inputs.asset.LoadBindingOverridesFromJson(bindingOverrides);
            _move = _inputs.asset.FindAction("Player/Move", true);
            _interact = _inputs.asset.FindAction("Player/Interact", true);
            _inventory = _inputs.asset.FindAction("Player/Inventory", true);
            _crafting = _inputs.asset.FindAction("Player/Crafting") ??
                        _inputs.asset.FindAction("Player/Crouch", true);
            _attack = _inputs.asset.FindAction("Player/Attack", true);
            _skill = _inputs.asset.FindAction("Player/Skill");
            
            _mousePosition  = _inputs.asset.FindAction("Player/MousePosition", true);
            
            _hotKeyPressed = _inputs.asset.FindAction("Player/Hotbar", true);
            _hotKeyPressed.performed += OnHotKeyPressed;
            
            // Keeps play mode usable until Unity regenerates Inputs after the
            // InputSystem_Actions asset gains the Skill action.
            if (_skill == null)
            {
                _skill = new InputAction("Skill", InputActionType.Button);
                _skill.AddBinding("<Keyboard>/q");
                _skill.AddBinding("<Gamepad>/rightShoulder");
                _skill.Enable();
                _ownsSkillAction = true;
            }

            _inputs.Player.Enable();
        }

        private int GetHotKeyPressed()
        {
            int hotKey = _pendingHotKey;
            _pendingHotKey = -1;
            return hotKey;
        }

        private void OnHotKeyPressed(InputAction.CallbackContext context)
        {
            if (context.ReadValueAsButton() && context.control is KeyControl key)
                _pendingHotKey = KeyToNumber(key.keyCode);
        }

        public void Tick()
        {
            InputContext context = CreateContext(out var movement);
            if (context.InteractionPressed || context.InventoryPressed ||
                context.CraftingPressed ||
                context.AttackPressed || context.SkillPressed ||
                context.HotBarPressed >= 0 || movement != lastMovement)
            {
                InputPerformed?.Invoke(context);
            }
            lastMovement = movement;
        }

        public void Dispose()
        {
            if (_inputs == null)
                return;

            _hotKeyPressed.performed -= OnHotKeyPressed;
            _inputs.Player.Disable();
            if (_ownsSkillAction)
            {
                _skill.Disable();
                _skill.Dispose();
            }
            _inputs.Dispose();
            _inputs = null;
        }

        private InputContext CreateContext(out Vector2 movement)
        {
            if (_inputs == null)
            {
                movement = Vector2.zero;
                return default;
            }
            
            movement = _move.ReadValue<Vector2>();

            return new InputContext(
                movement,
                _interact.WasPressedThisFrame(),
                _inventory.WasPressedThisFrame(),
                _crafting.WasPressedThisFrame(),
                _attack.WasPressedThisFrame(),
                _skill.WasPressedThisFrame(),
                GetHotKeyPressed());
        }

        private static int KeyToNumber(Key key)
        {
            Debug.Log($"Key: {key}");
            return key switch
            {
                Key.Digit1 => 0,
                Key.Digit2 => 1,
                Key.Digit3 => 2,
                Key.Digit4 => 3,
                Key.Digit5 => 4,
                Key.Digit6 => 5,
                Key.Digit7 => 6,
                Key.Digit8 => 7,
                Key.Digit9 => 8,
                Key.Digit0 => 9,
                _ => -1
            };
        }
    }
}
