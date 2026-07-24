using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    [CreateAssetMenu(fileName = "New Place Tile Item Action", menuName = "Data/Item Actions/Place Tile")]
    public sealed class PlaceTileItemAction : ItemAction
    {
        [SerializeField] private TileData tile;
        [SerializeField] private PersistentTileLayer layer = PersistentTileLayer.Ground;

        public override bool CanPerform(ItemActionContext context)
        {
            return TryGetTarget(context, out _, out _);
        }

        public override bool Perform(ItemActionContext context)
        {
            return TryGetTarget(context, out Chunk chunk, out Vector3Int cell) &&
                   chunk.TryPlaceTile(cell, layer, tile);
        }

        private bool TryGetTarget(
            ItemActionContext context,
            out Chunk chunk,
            out Vector3Int cell)
        {
            cell = Vector3Int.FloorToInt(context.TargetPosition);
            chunk = null;

            if (tile == null || context.User == null)
                return false;

            Chunkloader chunkloader = FindFirstObjectByType<Chunkloader>();
            return chunkloader != null &&
                   chunkloader.TryGetLoadedChunk(cell, out chunk);
        }
    }
}
