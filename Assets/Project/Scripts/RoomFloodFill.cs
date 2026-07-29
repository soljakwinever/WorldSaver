using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts
{
    public enum RoomCellKind : byte
    {
        Open = 0,
        Boundary = 1,
        Unavailable = 2
    }

    public enum RoomFloodStatus : byte
    {
        Enclosed = 0,
        Open = 1,
        TooLarge = 2
    }

    public sealed class RoomFloodResult
    {
        internal readonly List<Vector3Int> MutableInterior = new();
        internal readonly HashSet<Vector3Int> MutableBoundary = new();

        public RoomFloodStatus Status { get; internal set; }
        public IReadOnlyList<Vector3Int> Interior => MutableInterior;
        public IReadOnlyCollection<Vector3Int> Boundary => MutableBoundary;

        internal void Clear()
        {
            Status = RoomFloodStatus.Open;
            MutableInterior.Clear();
            MutableBoundary.Clear();
        }
    }

    /// <summary>
    /// Allocation-conscious four-way flood fill. One instance is intended to
    /// be reused serially by the room coordinator.
    /// </summary>
    public sealed class RoomFloodFill
    {
        private static readonly Vector3Int[] Directions =
        {
            Vector3Int.left,
            Vector3Int.right,
            Vector3Int.down,
            Vector3Int.up
        };

        private readonly Queue<Vector3Int> _frontier = new();
        private readonly HashSet<Vector3Int> _visited = new();
        private readonly RoomFloodResult _result = new();

        public RoomFloodResult Evaluate(
            Vector3Int seed,
            int maximumArea,
            Func<Vector3Int, RoomCellKind> classify)
        {
            if (classify == null)
                throw new ArgumentNullException(nameof(classify));
            if (maximumArea < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumArea));

            _frontier.Clear();
            _visited.Clear();
            _result.Clear();

            RoomCellKind seedKind = classify(seed);
            if (seedKind != RoomCellKind.Open)
            {
                _result.Status = RoomFloodStatus.Open;
                return _result;
            }

            bool reachesUnavailable = false;
            _frontier.Enqueue(seed);
            _visited.Add(seed);

            while (_frontier.Count > 0)
            {
                Vector3Int cell = _frontier.Dequeue();
                _result.MutableInterior.Add(cell);
                if (_result.MutableInterior.Count > maximumArea)
                {
                    _result.Status = RoomFloodStatus.TooLarge;
                    return _result;
                }

                for (int i = 0; i < Directions.Length; i++)
                {
                    Vector3Int neighbor = cell + Directions[i];
                    RoomCellKind kind = classify(neighbor);
                    if (kind == RoomCellKind.Boundary)
                    {
                        _result.MutableBoundary.Add(neighbor);
                        continue;
                    }

                    if (kind == RoomCellKind.Unavailable)
                    {
                        reachesUnavailable = true;
                        continue;
                    }

                    if (_visited.Add(neighbor))
                        _frontier.Enqueue(neighbor);
                }
            }

            _result.Status = reachesUnavailable
                ? RoomFloodStatus.Open
                : RoomFloodStatus.Enclosed;
            return _result;
        }
    }

    public static class RoomRoofPadding
    {
        /// <summary>
        /// Expands the room interior by one tile on every side and by one
        /// additional tile upward. Roof coverage, roof fading, and the
        /// visibility mask must all use this same footprint.
        /// </summary>
        public static void ExpandRoofAndMask(
            IReadOnlyList<Vector3Int> source,
            HashSet<Vector3Int> destination)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            destination.Clear();
            for (int i = 0; i < source.Count; i++)
            {
                Vector3Int cell = source[i];
                for (int y = -1; y <= 2; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        destination.Add(
                            cell + new Vector3Int(x, y, 0));
                    }
                }
            }
        }
    }
}
