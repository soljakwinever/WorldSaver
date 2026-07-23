using System;
using Project.Scripts.Interface;
using UnityEngine;
using UnityEngine.InputSystem;
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
        private InputAction _attack;
        private InputAction _skill;
        private bool _ownsSkillAction;

        public InputContext Context => CreateContext(out _);
        public event Action<InputContext> InputPerformed;

        public void Initialize()
        {
            _inputs = new Inputs();
            _move = _inputs.asset.FindAction("Player/Move", true);
            _interact = _inputs.asset.FindAction("Player/Interact", true);
            _inventory = _inputs.asset.FindAction("Player/Inventory", true);
            _attack = _inputs.asset.FindAction("Player/Attack", true);
            _skill = _inputs.asset.FindAction("Player/Skill");

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

        public void Tick()
        {
            InputContext context = CreateContext(out var movement);
            if (context.InteractionPressed || context.InventoryPressed ||
                context.AttackPressed || context.SkillPressed || movement != lastMovement)
            {
                InputPerformed?.Invoke(context);
            }
            lastMovement = movement;
        }

        public void Dispose()
        {
            if (_inputs == null)
                return;

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
                _attack.WasPressedThisFrame(),
                _skill.WasPressedThisFrame());
        }
    }
}
