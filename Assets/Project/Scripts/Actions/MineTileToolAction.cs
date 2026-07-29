using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;

namespace Project.Scripts.Actions
{
    /// <summary>
    /// Mines a tile using rules stored on the item as
    /// <see cref="MineTileToolActionData"/>.
    /// </summary>
    [CreateAssetMenu(
        fileName = "New Mine Tile Tool Action",
        menuName = "Data/Tool Actions/Mine Tile")]
    public sealed class MineTileToolAction : ToolAction
    {
        public override bool CanPerform(ToolActionContext context)
        {
            return TryGetTarget(
                context,
                out _,
                out _,
                out _,
                out _,
                out _);
        }

        public override bool Perform(ToolActionContext context)
        {
            if (!TryGetTarget(
                    context,
                    out Chunk chunk,
                    out Vector3Int cell,
                    out MineTileToolActionData data,
                    out TileData tile,
                    out PersistentTileLayer layer) ||
                !chunk.TryReplaceMinedTile(cell, layer))
            {
                return false;
            }

            TrySpawnDrop(context, tile, cell + new Vector3(0.5f, 0.5f));
            return true;
        }

        private bool TryGetTarget(
            ToolActionContext context,
            out Chunk chunk,
            out Vector3Int cell,
            out MineTileToolActionData data,
            out TileData targetTile,
            out PersistentTileLayer targetLayer)
        {
            cell = Vector3Int.FloorToInt(context.TargetPosition);
            chunk = null;
            data = null;
            targetTile = null;
            targetLayer = PersistentTileLayer.Ground;

            Chunkloader chunkloader = FindFirstObjectByType<Chunkloader>();
            if (context.User == null ||
                context.Item == null ||
                !context.Item.TryGetActionData(out data) ||
                chunkloader == null ||
                !chunkloader.TryGetLoadedChunk(cell, out chunk))
            {
                return false;
            }

            if (chunk.TryGetTileData(
                    cell,
                    PersistentTileLayer.Wall,
                    out targetTile))
            {
                targetLayer = PersistentTileLayer.Wall;
            }
            else if (!chunk.TryGetTileData(
                         cell,
                         PersistentTileLayer.Ground,
                         out targetTile))
            {
                return false;
            }

            // No tags means every selected tile is mineable.
            if (data.mineableTags == null || data.mineableTags.Length == 0)
                return true;

            foreach (EntityTag mineableTag in data.mineableTags)
            {
                if (targetTile.HasTag(mineableTag))
                {
                    return true;
                }
            }

            return false;
        }

        private static void TrySpawnDrop(
            ToolActionContext context,
            TileData tile,
            Vector3 position)
        {
            if (context.SpawnItemDrop == null ||
                tile?.droppedItem == null ||
                tile.dropChance <= 0f ||
                (tile.dropChance < 1f && Random.value >= tile.dropChance))
            {
                return;
            }

            context.SpawnItemDrop(tile.droppedItem, position);
        }
    }
}
