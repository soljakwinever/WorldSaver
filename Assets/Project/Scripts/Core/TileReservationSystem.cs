using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.Core
{
    /// <summary>
    /// Tracks the world-grid cells reserved by loaded entities.
    /// Reservations are runtime state: entity components recreate them when a
    /// chunk loads and release them when the entity or chunk is removed.
    /// </summary>
    public static class TileReservationSystem
    {
        private static readonly Dictionary<Vector2Int, object> OwnersByCell =
            new();
        private static readonly Dictionary<object, HashSet<Vector2Int>>
            CellsByOwner = new();

        public static bool IsReserved(
            Vector2Int cell,
            object ignoredOwner = null)
        {
            return OwnersByCell.TryGetValue(cell, out object owner) &&
                   !ReferenceEquals(owner, ignoredOwner);
        }

        public static bool CanReserve(RectInt area, object owner = null)
        {
            if (area.width <= 0 || area.height <= 0)
                return false;

            foreach (Vector2Int cell in area.allPositionsWithin)
            {
                if (OwnersByCell.TryGetValue(cell, out object existing) &&
                    !ReferenceEquals(existing, owner))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool TryReserve(object owner, RectInt area)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            if (!CanReserve(area, owner))
                return false;

            Release(owner);

            HashSet<Vector2Int> cells = new();
            foreach (Vector2Int cell in area.allPositionsWithin)
            {
                OwnersByCell[cell] = owner;
                cells.Add(cell);
            }

            CellsByOwner[owner] = cells;
            return true;
        }

        public static void Release(object owner)
        {
            if (owner == null ||
                !CellsByOwner.Remove(
                    owner,
                    out HashSet<Vector2Int> cells))
            {
                return;
            }

            foreach (Vector2Int cell in cells)
            {
                if (OwnersByCell.TryGetValue(cell, out object existing) &&
                    ReferenceEquals(existing, owner))
                {
                    OwnersByCell.Remove(cell);
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Clear()
        {
            OwnersByCell.Clear();
            CellsByOwner.Clear();
        }
    }
}
