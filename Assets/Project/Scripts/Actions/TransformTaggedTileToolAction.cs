using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;

namespace Project.Scripts.Actions
{
    /// <summary>
    /// Replaces a tile when its TileData carries an item-configured tag. The
    /// normal Chunk placement path records the replacement as a tile override.
    /// </summary>
    [CreateAssetMenu(
        fileName = "Transform Tagged Tile Tool Action",
        menuName = "Data/Tool Actions/Transform Tagged Tile")]
    public sealed class TransformTaggedTileToolAction :
        ToolAction,
        IRequiresToolProximity
    {
        [SerializeField, Min(0f)] private float interactionRange = 1.75f;

        public bool IsWithinToolRange(ToolActionContext context)
        {
            if (context.User == null)
                return false;

            Vector3Int cell = Vector3Int.FloorToInt(context.TargetPosition);
            Vector2 center = new(cell.x + 0.5f, cell.y + 0.5f);
            return ((Vector2)context.User.transform.position - center)
                   .sqrMagnitude <= interactionRange * interactionRange;
        }

        public override bool CanPerform(ToolActionContext context) =>
            TryGetTarget(context, out _, out _, out _);

        public override bool Perform(ToolActionContext context)
        {
            return TryGetTarget(
                       context,
                       out Chunk chunk,
                       out Vector3Int cell,
                       out TransformTaggedTileToolActionData data) &&
                   chunk.TryPlaceTile(cell, data.layer, data.replacementTile);
        }

        private static bool TryGetTarget(
            ToolActionContext context,
            out Chunk chunk,
            out Vector3Int cell,
            out TransformTaggedTileToolActionData data)
        {
            cell = Vector3Int.FloorToInt(context.TargetPosition);
            chunk = null;
            data = null;

            Chunkloader loader = FindAnyObjectByType<Chunkloader>();
            if (context.User == null ||
                context.Item == null ||
                !context.Item.TryGetActionData(out data) ||
                data.requiredTag == null ||
                data.replacementTile == null ||
                loader == null ||
                !loader.TryGetLoadedChunk(cell, out chunk) ||
                !chunk.TryGetTileData(cell, data.layer, out TileData source))
            {
                return false;
            }

            return source != null &&
                   source != data.replacementTile &&
                   source.HasTag(data.requiredTag) &&
                   ReplacementUsesLayer(data.replacementTile, data.layer);
        }

        internal static bool ReplacementUsesLayer(
            TileData replacement,
            PersistentTileLayer layer)
        {
            return replacement != null &&
                   replacement.IsWall == (layer == PersistentTileLayer.Wall);
        }
    }
}
