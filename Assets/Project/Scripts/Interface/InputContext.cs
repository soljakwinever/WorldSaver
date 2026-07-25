using UnityEngine;

namespace Project.Scripts.Interface
{
    public readonly struct InputContext
    {
        public const int NoHotbarKeyPressed = -1;
        public Vector2 Movement { get; }
        public bool InteractionPressed { get; }
        public bool InventoryPressed { get; }
        public bool CraftingPressed { get; }
        public bool AttackPressed { get; }
        public bool SkillPressed { get; }
        public int HotBarPressed { get; }

        public InputContext(Vector2 movement,
            bool interactionPressed,
            bool inventoryPressed,
            bool craftingPressed,
            bool attackPressed,
            bool skillPressed,
            int hotBarPressed)
        {
            Movement = movement;
            InteractionPressed = interactionPressed;
            InventoryPressed = inventoryPressed;
            CraftingPressed = craftingPressed;
            AttackPressed = attackPressed;
            SkillPressed = skillPressed;
            HotBarPressed = hotBarPressed;
        }
    }
}
