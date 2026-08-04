using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using UnityEngine;

namespace Project.Scripts.Actions
{
    /// <summary>Consumes an item to restore the user's Health or Mana.</summary>
    [CreateAssetMenu(
        fileName = "New Increase Health Mana Item Action",
        menuName = "Data/Item Actions/Increase Health or Mana")]
    public sealed class IncreaseHealthManaItemAction : ItemAction
    {
        public override bool ConsumesItem => true;
        public override bool DisplayCount => true;
        public override int GetCount(ItemData item) => GetItemCount(item);
        
        private PersistentInventory _playerInventory;

        public override bool CanPerform(ActionContext context)
        {
            if (!TryGetData(context, out IncreaseHealthManaActionData data))
                return false;

            IHasHealth health = context.User.GetComponent<IHasHealth>();
            IHasMana mana = context.User.GetComponent<IHasMana>();
            return CanIncreaseHealth(data, health) ||
                   CanIncreaseMana(data, mana);
        }

        public override bool Perform(ActionContext context)
        {
            if (!TryGetData(context, out IncreaseHealthManaActionData data))
                return false;

            bool increased = false;
            IHasHealth health = context.User.GetComponent<IHasHealth>();
            if (CanIncreaseHealth(data, health))
            {
                int previousHealth = health.Health;
                health.Heal(data.health);
                increased |= health.Health > previousHealth;
            }

            IHasMana mana = context.User.GetComponent<IHasMana>();
            if (CanIncreaseMana(data, mana))
            {
                int previousMana = mana.CurrentMana;
                mana.RestoreMana(data.mana);
                increased |= mana.CurrentMana > previousMana;
            }

            return increased;
        }

        private static bool TryGetData(
            ActionContext context,
            out IncreaseHealthManaActionData data)
        {
            data = null;
            return context.User != null &&
                   context.Item != null &&
                   context.Item.TryGetActionData(out data);
        }

        private static bool CanIncreaseHealth(
            IncreaseHealthManaActionData data,
            IHasHealth health)
        {
            return data.health > 0 &&
                   health != null &&
                   health.Health > 0 &&
                   health.Health < health.MaxHealth;
        }

        private static bool CanIncreaseMana(
            IncreaseHealthManaActionData data,
            IHasMana mana)
        {
            return data.mana > 0 &&
                   mana != null &&
                   mana.CurrentMana < mana.MaxMana;
        }
        
        private int GetItemCount(ItemData item)
        {
            if (item == null)
                return 0;

            if (_playerInventory == null)
            {
                PlayerInteractionController player =
                    FindFirstObjectByType<PlayerInteractionController>();
                if (player != null)
                    _playerInventory =
                        player.GetComponent<PersistentInventory>();
            }

            if (_playerInventory == null)
                return 0;

            // The count is display-only; consumption is handled by the player.
            int count = 0;
            foreach (IItemStack stack in _playerInventory.Stacks)
            {
                if (stack.Item == item)
                    count = checked(count + stack.Count);
            }

            return count;
        }
    }
}
