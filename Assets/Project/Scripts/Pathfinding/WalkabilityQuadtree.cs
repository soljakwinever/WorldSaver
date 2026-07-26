using System;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Pathfinding
{
    /// <summary>
    /// Sparse point quadtree over a fixed signed-int subset. Nodes are created
    /// only for queried cells, keeping lookups independent of loaded chunks.
    /// </summary>
    internal sealed class WalkabilityQuadtree
    {
        private readonly IPathFindingMap _map;
        private readonly int _depth;
        private Node _root = new();

        public WalkabilityQuadtree(IPathFindingMap map, int depth)
        {
            if (depth < 1 || depth > 30)
                throw new ArgumentOutOfRangeException(nameof(depth));
            _map = map;
            _depth = depth;
        }

        public bool IsWalkable(Vector2Int position)
        {
            Node node = Find(position, true);
            if (!node.HasValue)
            {
                node.Value = _map.IsWalkable(position);
                node.HasValue = true;
            }
            return node.Value;
        }

        public void Invalidate(Vector2Int position)
        {
            Node node = Find(position, false);
            if (node != null)
                node.HasValue = false;
        }

        public void Clear() => _root = new Node();

        private Node Find(Vector2Int position, bool create)
        {
            int offset = 1 << (_depth - 1);
            long normalizedX = (long)position.x + offset;
            long normalizedY = (long)position.y + offset;
            int size = 1 << _depth;
            if (normalizedX < 0 || normalizedY < 0 ||
                normalizedX >= size || normalizedY >= size)
                throw new ArgumentOutOfRangeException(
                    nameof(position),
                    $"Cell must be within [{-offset}, {offset - 1}] on both axes.");

            Node node = _root;
            for (int bit = _depth - 1; bit >= 0; bit--)
            {
                int quadrant =
                    (int)((normalizedX >> bit) & 1L) |
                    (int)(((normalizedY >> bit) & 1L) << 1);
                Node child = node.Children?[quadrant];
                if (child == null)
                {
                    if (!create)
                        return null;
                    node.Children ??= new Node[4];
                    child = node.Children[quadrant] = new Node();
                }
                node = child;
            }
            return node;
        }

        private sealed class Node
        {
            public Node[] Children;
            public bool HasValue;
            public bool Value;
        }
    }
}
