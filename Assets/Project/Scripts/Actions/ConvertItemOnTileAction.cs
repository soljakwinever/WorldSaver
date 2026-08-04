using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Actions
{
    /// <summary>
    /// Atomically replaces one selected item when Direct Interact is pressed
    /// while its owner occupies the configured tile.
    /// </summary>
    [CreateAssetMenu(
        fileName = "Convert Item On Tile Action",
        menuName = "Data/Item Actions/Convert Item On Tile")]
    public sealed class ConvertItemOnTileAction :
        ItemAction,
        IDirectTileInteractionItemAction
    {
        public override bool CanPerform(ActionContext context) =>
            TryGetConversion(context, out IInventory inventory, out _,
                out IReadOnlyList<InventoryChange> changes) &&
            inventory.CanApplyChanges(changes);

        public override bool Perform(ActionContext context) =>
            TryGetConversion(context, out IInventory inventory, out _,
                out IReadOnlyList<InventoryChange> changes) &&
            inventory.TryApplyChanges(changes);

        private static bool TryGetConversion(
            ActionContext context,
            out IInventory inventory,
            out ConvertItemOnTileActionData data,
            out IReadOnlyList<InventoryChange> changes)
        {
            inventory = null;
            data = null;
            changes = null;

            if (context.User == null ||
                context.Item == null ||
                !context.Item.TryGetActionData(out data) ||
                data.requiredTile == null ||
                data.replacementItem == null ||
                data.replacementItem == context.Item)
            {
                return false;
            }

            inventory = context.User.GetComponentInParent<PersistentInventory>();
            if (inventory == null)
                return false;

            IItemStack sourceStack = null;
            foreach (IItemStack stack in inventory.Stacks)
            {
                if (stack.Item == context.Item && stack.Count > 0)
                {
                    sourceStack = stack;
                    break;
                }
            }

            if (sourceStack == null)
                return false;

            Vector3Int cell = Vector3Int.FloorToInt(context.TargetPosition);
            Chunkloader loader = FindAnyObjectByType<Chunkloader>();
            if (loader == null ||
                !loader.TryGetLoadedChunk(cell, out Chunk chunk) ||
                !chunk.HasTile(cell, data.layer, data.requiredTile))
            {
                return false;
            }

            changes = new[]
            {
                new InventoryChange(
                    context.Item,
                    -1,
                    sourceStack.Rarity,
                    sourceStack.Durability),
                new InventoryChange(
                    data.replacementItem,
                    1,
                    sourceStack.Rarity)
            };
            return true;
        }
    }
}
