using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Actions
{
    [CreateAssetMenu(
        fileName = "Equip Item Action",
        menuName = "Actions/Items/Equip Item")]
    public sealed class EquipItemAction : ItemAction
    {
        public override bool CanPerform(ActionContext context)
        {
            if (!TryGetControllers(
                    context,
                    out PlayerEquipmentController equipment,
                    out PersistentInventory inventory) ||
                context.Item is not EquipableItemData equipable)
                return false;

            if (equipment.TryGetEquipped(
                    equipable.EquipmentSlot,
                    out IItemStack equipped) &&
                equipped.Item == context.Item)
                return true;

            return FindStack(inventory, context.Item) != null;
        }

        public override bool Perform(ActionContext context)
        {
            if (!TryGetControllers(
                    context,
                    out PlayerEquipmentController equipment,
                    out PersistentInventory inventory) ||
                context.Item is not EquipableItemData equipable)
                return false;

            if (equipment.TryGetEquipped(
                    equipable.EquipmentSlot,
                    out IItemStack equipped) &&
                equipped.Item == context.Item)
                return equipment.TryUnequip(equipable.EquipmentSlot);

            IItemStack stack = FindStack(inventory, context.Item);
            return stack != null && equipment.TryEquip(stack);
        }

        private static IItemStack FindStack(
            PersistentInventory inventory,
            ItemData item)
        {
            foreach (IItemStack stack in inventory.Stacks)
            {
                if (stack.Item == item)
                    return stack;
            }

            return null;
        }

        private static bool TryGetControllers(
            ActionContext context,
            out PlayerEquipmentController equipment,
            out PersistentInventory inventory)
        {
            equipment = null;
            inventory = null;
            if (context.User == null)
                return false;

            equipment = context.User
                .GetComponentInParent<PlayerEquipmentController>();
            inventory = context.User
                .GetComponentInParent<PersistentInventory>();
            return equipment != null && inventory != null;
        }
    }
}
