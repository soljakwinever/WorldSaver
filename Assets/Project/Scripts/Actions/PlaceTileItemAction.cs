using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Actions
{
    /// <summary>
    /// Places the tile described by <see cref="PlaceTileItemActionData"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "New Place Tile Item Action", menuName = "Data/Item Actions/Place Tile")]
    public sealed class PlaceTileItemAction : ItemAction
    {
        private PersistentInventory _playerInventory;

        public override string GetPersistentId(ItemData item) =>
            TryGetData(item, out PlaceTileItemActionData data) &&
            data.tile != null
                ? data.tile.TileId.ToString()
                : base.GetPersistentId(item);
        public override string GetDisplayName(ItemData item) =>
            TryGetData(item, out PlaceTileItemActionData data) &&
            data.tile != null
                ? $"Place Tile {data.tile.name}"
                : base.GetDisplayName(item);
        public override string GetTooltip(ItemData item) =>
            GetDisplayName(item);
        public override int GetCount(ItemData item) => GetItemCount(item);
        public override float Refresh => 0.1f;
        public override bool DisplayCount => true;

        public override bool CanPerform(ActionContext context)
        {
            return TryGetTarget(context, out _, out _, out _);
        }

        public override bool Perform(ActionContext context)
        {
            return TryGetTarget(
                       context,
                       out Chunk chunk,
                       out Vector3Int cell,
                       out PlaceTileItemActionData data) &&
                   !chunk.HasTile(cell, data.layer, data.tile) &&
                   chunk.TryPlaceTile(cell, data.layer, data.tile);
        }

        private bool TryGetTarget(
            ActionContext context,
            out Chunk chunk,
            out Vector3Int cell,
            out PlaceTileItemActionData data)
        {
            cell = Vector3Int.FloorToInt(context.TargetPosition);
            chunk = null;
            data = null;

            if (context.User == null ||
                !TryGetData(context.Item, out data) ||
                data.tile == null)
                return false;

            Chunkloader chunkloader = FindFirstObjectByType<Chunkloader>();
            return chunkloader != null &&
                   chunkloader.TryGetLoadedChunk(cell, out chunk);
        }

        private static bool TryGetData(
            ItemData item,
            out PlaceTileItemActionData data)
        {
            data = null;
            return item != null && item.TryGetActionData(out data);
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
