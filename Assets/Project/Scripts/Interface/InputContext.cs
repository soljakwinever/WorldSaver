using UnityEngine;

namespace Project.Scripts.Interface
{
    public readonly struct InputContext
    {
        public Vector2 Movement { get; }
        public bool InteractionPressed { get; }
        public bool InventoryPressed { get; }
        public bool AttackPressed { get; }
        public bool SkillPressed { get; }

        public InputContext(
            Vector2 movement,
            bool interactionPressed,
            bool inventoryPressed,
            bool attackPressed,
            bool skillPressed)
        {
            Movement = movement;
            InteractionPressed = interactionPressed;
            InventoryPressed = inventoryPressed;
            AttackPressed = attackPressed;
            SkillPressed = skillPressed;
        }
    }
}
