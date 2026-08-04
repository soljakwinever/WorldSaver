using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.Persistence;
using UnityEngine;

namespace Project.Scripts
{
    public static class SpaceReservationUtility
    {
        /// <summary>
        /// Converts a tile-anchored placement position to the entity's visual
        /// position. Entities with a reservation sit at the footprint's center;
        /// all other entities sit at the center of their placement tile.
        /// </summary>
        public static Vector2 GetEntityPosition(
            NodeData nodeData,
            Vector2 placementPosition)
        {
            Vector2Int anchor = Vector2Int.FloorToInt(placementPosition);
            if (TryGetData(nodeData, out SpaceReservationData data))
            {
                Vector2 origin = anchor +
                                 new Vector2(data.xOffset, data.yOffset);
                return origin + new Vector2(
                    Mathf.Max(1, data.width) * 0.5f,
                    Mathf.Max(1, data.height) * 0.5f);
            }

            return (Vector2)anchor + Vector2.one * 0.5f;
        }

        public static bool CanPlace(NodeData nodeData, Vector2 position)
        {
            Vector2Int anchor = Vector2Int.FloorToInt(position);
            if (TileReservationSystem.IsReserved(anchor))
                return false;

            return !TryGetArea(nodeData, position, out RectInt area) ||
                   TileReservationSystem.CanReserve(area);
        }

        public static bool TryGetArea(
            NodeData nodeData,
            Vector2 position,
            out RectInt area)
        {
            if (TryGetData(nodeData, out SpaceReservationData data))
            {
                Vector2Int origin =
                    Vector2Int.FloorToInt(position) +
                    new Vector2Int(data.xOffset, data.yOffset);
                area = new RectInt(
                    origin,
                    new Vector2Int(
                        Mathf.Max(1, data.width),
                        Mathf.Max(1, data.height)));
                return true;
            }

            area = default;
            return false;
        }

        private static bool TryGetData(
            NodeData nodeData,
            out SpaceReservationData reservation)
        {
            reservation = null;
            if (nodeData?.persistentComponents == null)
                return false;

            foreach (ComponentDefinitionData data
                     in nodeData.persistentComponents)
            {
                if (data is SpaceReservationData candidate)
                {
                    reservation = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}
