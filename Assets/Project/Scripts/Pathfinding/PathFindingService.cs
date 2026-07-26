using System;
using System.Collections.Generic;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Pathfinding
{
    /// <summary>
    /// Tile A* backed by a sparse quadtree cache (the two-dimensional form of
    /// an octree). World coordinates are used throughout, so chunk boundaries
    /// and negative chunk coordinates need no special cases.
    /// </summary>
    public sealed class PathFindingService : IPathFindingService
    {
        private const float DiagonalCost = 1.41421356f;
        private readonly IPathFindingMap _map;
        private readonly WalkabilityQuadtree _walkability;
        private readonly bool _allowDiagonals;

        public PathFindingService(
            IPathFindingMap map,
            bool allowDiagonals = false,
            int spatialIndexDepth = 30)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _allowDiagonals = allowDiagonals;
            _walkability = new WalkabilityQuadtree(map, spatialIndexDepth);
        }

        public bool TryFindPath(
            Vector2Int start,
            Vector2Int destination,
            List<Vector2Int> path,
            int maxVisitedTiles = 100000)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (maxVisitedTiles <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxVisitedTiles));

            path.Clear();
            if (!_walkability.IsWalkable(start) ||
                !_walkability.IsWalkable(destination))
                return false;

            if (start == destination)
            {
                path.Add(start);
                return true;
            }

            MinHeap open = new();
            Dictionary<Vector2Int, NodeRecord> records = new();
            HashSet<Vector2Int> closed = new();
            long sequence = 0;

            records[start] = new NodeRecord(0f, start, false);
            open.Push(new OpenNode(start, Heuristic(start, destination), 0f, sequence++));

            int visited = 0;
            while (open.Count > 0 && visited < maxVisitedTiles)
            {
                OpenNode currentEntry = open.Pop();
                if (closed.Contains(currentEntry.Position) ||
                    !records.TryGetValue(currentEntry.Position, out NodeRecord current) ||
                    currentEntry.CostFromStart > current.Cost + 0.0001f)
                    continue;

                if (currentEntry.Position == destination)
                {
                    BuildPath(destination, records, path);
                    return true;
                }

                closed.Add(currentEntry.Position);
                visited++;

                VisitNeighbours(
                    currentEntry.Position,
                    destination,
                    current.Cost,
                    records,
                    closed,
                    open,
                    ref sequence);
            }

            return false;
        }

        /// <summary>Invalidates cached navigation data after a world edit.</summary>
        public void Invalidate(Vector2Int worldCell) => _walkability.Invalidate(worldCell);

        /// <summary>Clears all spatially cached walkability values.</summary>
        public void InvalidateAll() => _walkability.Clear();

        private void VisitNeighbours(
            Vector2Int current,
            Vector2Int destination,
            float currentCost,
            Dictionary<Vector2Int, NodeRecord> records,
            HashSet<Vector2Int> closed,
            MinHeap open,
            ref long sequence)
        {
            int count = _allowDiagonals ? 8 : 4;
            for (int i = 0; i < count; i++)
            {
                Vector2Int offset = Directions[i];
                Vector2Int next = current + offset;
                if (closed.Contains(next) || !_walkability.IsWalkable(next))
                    continue;

                bool diagonal = offset.x != 0 && offset.y != 0;
                if (diagonal &&
                    (!_walkability.IsWalkable(current + new Vector2Int(offset.x, 0)) ||
                     !_walkability.IsWalkable(current + new Vector2Int(0, offset.y))))
                    continue;

                float traversalCost = _map.GetTraversalCost(next);
                if (float.IsNaN(traversalCost) ||
                    float.IsInfinity(traversalCost) ||
                    traversalCost < 0f)
                    continue;

                // Costs are multipliers: values below one would make the
                // distance heuristic overestimate and break A* optimality.
                float candidate = currentCost +
                                  (diagonal ? DiagonalCost : 1f) *
                                  Math.Max(1f, traversalCost);
                if (records.TryGetValue(next, out NodeRecord old) &&
                    candidate >= old.Cost)
                    continue;

                records[next] = new NodeRecord(candidate, current, true);
                float priority = candidate + Heuristic(next, destination);
                open.Push(new OpenNode(next, priority, candidate, sequence++));
            }
        }

        private float Heuristic(Vector2Int from, Vector2Int to)
        {
            int dx = Math.Abs(from.x - to.x);
            int dy = Math.Abs(from.y - to.y);
            return _allowDiagonals
                ? Math.Max(dx, dy) + (DiagonalCost - 1f) * Math.Min(dx, dy)
                : dx + dy;
        }

        private static void BuildPath(
            Vector2Int destination,
            Dictionary<Vector2Int, NodeRecord> records,
            List<Vector2Int> path)
        {
            Vector2Int current = destination;
            while (true)
            {
                path.Add(current);
                NodeRecord record = records[current];
                if (!record.HasParent)
                    break;
                current = record.Parent;
            }

            path.Reverse();
        }

        private static readonly Vector2Int[] Directions =
        {
            Vector2Int.right,
            Vector2Int.up,
            Vector2Int.left,
            Vector2Int.down,
            new(1, 1),
            new(-1, 1),
            new(-1, -1),
            new(1, -1)
        };

        private readonly struct NodeRecord
        {
            public readonly float Cost;
            public readonly Vector2Int Parent;
            public readonly bool HasParent;

            public NodeRecord(float cost, Vector2Int parent, bool hasParent)
            {
                Cost = cost;
                Parent = parent;
                HasParent = hasParent;
            }
        }

        private readonly struct OpenNode
        {
            public readonly Vector2Int Position;
            public readonly float Priority;
            public readonly float CostFromStart;
            public readonly long Sequence;

            public OpenNode(
                Vector2Int position,
                float priority,
                float costFromStart,
                long sequence)
            {
                Position = position;
                Priority = priority;
                CostFromStart = costFromStart;
                Sequence = sequence;
            }
        }

        private sealed class MinHeap
        {
            private readonly List<OpenNode> _items = new();
            public int Count => _items.Count;

            public void Push(OpenNode value)
            {
                _items.Add(value);
                int index = _items.Count - 1;
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (!Less(value, _items[parent]))
                        break;
                    _items[index] = _items[parent];
                    index = parent;
                }
                _items[index] = value;
            }

            public OpenNode Pop()
            {
                OpenNode result = _items[0];
                int lastIndex = _items.Count - 1;
                OpenNode last = _items[lastIndex];
                _items.RemoveAt(lastIndex);
                if (_items.Count == 0)
                    return result;

                int index = 0;
                while (true)
                {
                    int left = index * 2 + 1;
                    if (left >= _items.Count)
                        break;
                    int right = left + 1;
                    int child = right < _items.Count && Less(_items[right], _items[left])
                        ? right
                        : left;
                    if (!Less(_items[child], last))
                        break;
                    _items[index] = _items[child];
                    index = child;
                }
                _items[index] = last;
                return result;
            }

            private static bool Less(OpenNode a, OpenNode b)
            {
                int priority = a.Priority.CompareTo(b.Priority);
                return priority < 0 ||
                       (priority == 0 && a.Sequence < b.Sequence);
            }
        }
    }
}
