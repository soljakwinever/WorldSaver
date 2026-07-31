using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
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
    public sealed class PlacePersistentNodeItemAction :
        ItemAction,
        IUsesCursor
    {
        private const float CursorOpacity = 0.55f;

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
            if (!TryGetTarget(
                    context,
                    out Chunk chunk,
                    out Vector2 position,
                    out NodeData node))
            {
                return false;
            }

            AccessIdentity identity =
                ResolveAccessIdentity(context.User, position);
            return chunk.TrySpawnRuntimeEntity(
                node,
                position,
                identity,
                out _);
        }

        public bool TryGetCursor(
            ActionContext context,
            out PlacementCursorData cursor)
        {
            if (!TryGetData(
                    context.Item,
                    out PlacePersistentNodeItemActionData data) ||
                data.node == null)
            {
                cursor = default;
                return false;
            }

            Sprite sprite = GetPreviewSprite(data.node, context.Item);
            if (sprite == null)
            {
                cursor = default;
                return false;
            }

            Vector2Int cell =
                Vector2Int.FloorToInt(context.TargetPosition);
            cursor = new PlacementCursorData(
                sprite,
                new Vector3(cell.x, cell.y, 0f),
                CanPerform(context),
                CursorOpacity);
            return true;
        }

        private static Sprite GetPreviewSprite(
            NodeData node,
            ItemData item)
        {
            if (node.sprite != null)
                return node.sprite;
            if (node.sprites is { Length: > 0 })
                return node.sprites[0];

            // Override-visual entities may not have a single NodeData sprite.
            // Their inventory art still gives placement a useful preview.
            return item != null ? item.sprite : null;
        }

        private static AccessIdentity ResolveAccessIdentity(
            GameObject user,
            Vector2 placementPosition)
        {
            PlayerDataController player =
                user != null
                    ? user.GetComponentInParent<PlayerDataController>()
                    : null;
            string ownerId = player != null
                ? player.PlayerId
                : string.Empty;

            IAiPathingAgent pathingAgent =
                user != null
                    ? user.GetComponentInParent<IAiPathingAgent>() ??
                      user.GetComponentInChildren<IAiPathingAgent>()
                    : null;
            PathFindingQuery affiliation =
                pathingAgent?.CapturePathFindingQuery() ?? default;
            string villageId = affiliation.VillageId;

            if (string.IsNullOrEmpty(villageId))
                villageId = FindContainingVillageId(placementPosition);

            return new AccessIdentity(
                ownerId,
                villageId,
                affiliation.FactionId);
        }

        private static string FindContainingVillageId(Vector2 position)
        {
            TownCore nearest = null;
            float nearestDistance = float.PositiveInfinity;
            foreach (TownCore town in TownCoreRegistry.All)
            {
                if (town == null ||
                    !town.ContainsTownPosition(position))
                    continue;

                float distance =
                    ((Vector2)town.Position - position).sqrMagnitude;
                if (distance >= nearestDistance)
                    continue;
                nearest = town;
                nearestDistance = distance;
            }

            if (nearest == null)
                return string.Empty;

            PersistentEntity entity =
                nearest.GetComponentInParent<PersistentEntity>();
            return entity != null
                ? entity.Id.ToString()
                : string.Empty;
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
                   SpaceReservationUtility.CanPlace(node, position) &&
                   !HasNodeInPlacementArea(node, position);
        }

        private static bool HasNodeInPlacementArea(
            NodeData nodeData,
            Vector2 position)
        {
            if (HasNodeAt(position))
                return true;

            if (!SpaceReservationUtility.TryGetArea(
                    nodeData,
                    position,
                    out RectInt area))
            {
                return false;
            }

            foreach (Vector2Int cell in area.allPositionsWithin)
            {
                if (cell != Vector2Int.FloorToInt(position) &&
                    HasNodeAt(cell))
                {
                    return true;
                }
            }

            return false;
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
