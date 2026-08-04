using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using UnityEngine;

namespace Project.Scripts.Actions
{
    /// <summary>Consumes an item to restore the user's Hunger or Energy.</summary>
    [CreateAssetMenu(
        fileName = "New Increase Needs Item Action",
        menuName = "Data/Item Actions/Increase Needs")]
    public sealed class IncreaseNeedsItemAction : ItemAction
    {
        public override bool ConsumesItem => true;

        public override int GetCount(ItemData item) => GetItemCount(item);

        public override bool DisplayCount => true;

        private PersistentInventory _playerInventory;
        
        public override bool CanPerform(ActionContext context)
        {
            return TryGetTargets(
                context,
                out IncreaseNeedsActionData data,
                out IHasNeeds needs) &&
                CanIncrease(data, needs);
        }

        public override bool Perform(ActionContext context)
        {
            if (!TryGetTargets(
                    context,
                    out IncreaseNeedsActionData data,
                    out IHasNeeds needs))
            {
                return false;
            }

            float previousHunger = needs.Hunger;
            float previousEnergy = needs.Energy;
            if (data.hunger > 0f)
                needs.Hunger = Mathf.Min(1f, previousHunger + data.hunger);
            if (data.energy > 0f)
                needs.Energy = Mathf.Min(1f, previousEnergy + data.energy);

            return needs.Hunger > previousHunger ||
                   needs.Energy > previousEnergy;
        }

        private static bool TryGetTargets(
            ActionContext context,
            out IncreaseNeedsActionData data,
            out IHasNeeds needs)
        {
            data = null;
            needs = null;
            if (context.User == null ||
                context.Item == null ||
                !context.Item.TryGetActionData(out data))
            {
                return false;
            }

            needs = context.User.GetComponent<IHasNeeds>();
            return needs != null;
        }

        private static bool CanIncrease(
            IncreaseNeedsActionData data,
            IHasNeeds needs)
        {
            return data.hunger > 0f && needs.Hunger < 1f ||
                   data.energy > 0f && needs.Energy < 1f;
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
