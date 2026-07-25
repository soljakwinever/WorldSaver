using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Actions
{
    /// <summary>
    /// Places a runtime-persistent node described by
    /// <see cref="PlacePersistentNodeItemActionData"/>.
    /// </summary>
    [CreateAssetMenu(
        fileName = "New Place Persistent Node Item Action",
        menuName = "Data/Item Actions/Place Persistent Node")]
    public sealed class PlacePersistentNodeItemAction : ItemAction
    {
        public override string GetDisplayName(ItemData item) =>
            TryGetData(item, out PlacePersistentNodeItemActionData data) &&
            data.node != null
                ? $"Place {data.node.name}"
                : base.GetDisplayName(item);

        public override string GetTooltip(ItemData item) =>
            GetDisplayName(item);

        public override bool CanPerform(ActionContext context)
        {
            return TryGetTarget(context, out _, out _, out _);
        }

        public override bool Perform(ActionContext context)
        {
            return TryGetTarget(
                       context,
                       out Chunk chunk,
                       out Vector2 position,
                       out NodeData node) &&
                   chunk.TrySpawnRuntimeEntity(node, position, out _);
        }

        private static bool TryGetTarget(
            ActionContext context,
            out Chunk chunk,
            out Vector2 position,
            out NodeData node)
        {
            position = Vector2Int.FloorToInt(context.TargetPosition);
            chunk = null;
            node = null;

            if (context.User == null ||
                !TryGetData(
                    context.Item,
                    out PlacePersistentNodeItemActionData data) ||
                data.node == null)
            {
                return false;
            }

            node = data.node;
            Chunkloader chunkloader =
                Object.FindFirstObjectByType<Chunkloader>();
            Vector3Int cell = Vector3Int.FloorToInt(context.TargetPosition);
            // Runtime persistence identifies nodes through EntityArchetype IDs.
            return chunkloader != null &&
                   chunkloader.TryGetLoadedChunk(cell, out chunk) &&
                   chunk.CanSpawnRuntimeEntity(node) &&
                   !HasNodeAt(position);
        }

        /// <summary>Prevents two node colliders from sharing a cell.</summary>
        private static bool HasNodeAt(Vector2 position)
        {
            Collider2D[] colliders = Physics2D.OverlapPointAll(position);
            foreach (Collider2D collider in colliders)
            {
                if (collider.GetComponentInParent<Node>() != null)
                    return true;
            }

            return false;
        }

        private static bool TryGetData(
            ItemData item,
            out PlacePersistentNodeItemActionData data)
        {
            data = null;
            return item != null && item.TryGetActionData(out data);
        }
    }
}
