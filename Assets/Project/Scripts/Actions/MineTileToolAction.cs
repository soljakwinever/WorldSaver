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
            return TryGetTarget(context, out _, out _, out _);
        }

        public override bool Perform(ToolActionContext context)
        {
            return TryGetTarget(
                       context,
                       out Chunk chunk,
                       out Vector3Int cell,
                       out MineTileToolActionData data) &&
                   chunk.TryReplaceMinedTile(cell, data.layer);
        }

        private bool TryGetTarget(
            ToolActionContext context,
            out Chunk chunk,
            out Vector3Int cell,
            out MineTileToolActionData data)
        {
            cell = Vector3Int.FloorToInt(context.TargetPosition);
            chunk = null;
            data = null;

            Chunkloader chunkloader = FindFirstObjectByType<Chunkloader>();
            if (context.User == null ||
                context.Item == null ||
                !context.Item.TryGetActionData(out data) ||
                chunkloader == null ||
                !chunkloader.TryGetLoadedChunk(cell, out chunk))
            {
                return false;
            }

            // No tags means every tile on the configured layer is mineable.
            if (data.mineableTags == null || data.mineableTags.Length == 0)
                return chunk.HasTile(cell, data.layer);

            if (!chunk.TryGetTileData(cell, data.layer, out TileData targetTile))
                return false;

            foreach (EntityTag mineableTag in data.mineableTags)
            {
                if (targetTile.HasTag(mineableTag))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
