using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.ProceduralGeneration
{
    /// <summary>Small deterministic A* used while generating feature roads.</summary>
    internal static class ProceduralRoadPathfinder
    {
        private static readonly Vector2Int[] Directions =
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
            new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)
        };

        public static Vector2[] Find(
            Vector2 start,
            Vector2 end,
            int step,
            Func<Vector2, float> traversalCost)
        {
            step = Mathf.Max(1, step);
            Vector2Int startNode = Round(start, step);
            Vector2Int endNode = Round(end, step);
            int directSteps = Mathf.CeilToInt(Vector2.Distance(startNode, endNode));
            int margin = Mathf.Max(4, directSteps / 3);
            int minX = Mathf.Min(startNode.x, endNode.x) - margin;
            int maxX = Mathf.Max(startNode.x, endNode.x) + margin;
            int minY = Mathf.Min(startNode.y, endNode.y) - margin;
            int maxY = Mathf.Max(startNode.y, endNode.y) + margin;

            var open = new MinHeap();
            var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
            var scores = new Dictionary<Vector2Int, float> { [startNode] = 0f };
            open.Push(startNode, Heuristic(startNode, endNode));

            while (open.Count > 0)
            {
                Vector2Int current = open.Pop();
                if (current == endNode)
                    return Reconstruct(cameFrom, current, start, end, step);

                float currentScore = scores[current];
                foreach (Vector2Int direction in Directions)
                {
                    Vector2Int next = current + direction;
                    if (next.x < minX || next.x > maxX ||
                        next.y < minY || next.y > maxY)
                        continue;

                    Vector2 world = (Vector2)next * step;
                    float terrainCost = traversalCost(world);
                    if (float.IsNaN(terrainCost) || float.IsInfinity(terrainCost))
                        continue;
                    float move = direction.x != 0 && direction.y != 0 ? 1.41421356f : 1f;
                    float tentative = currentScore + move + Mathf.Max(0f, terrainCost);
                    if (scores.TryGetValue(next, out float old) && tentative >= old)
                        continue;

                    cameFrom[next] = current;
                    scores[next] = tentative;
                    open.Push(next, tentative + Heuristic(next, endNode));
                }
            }

            return Array.Empty<Vector2>();
        }

        private static Vector2Int Round(Vector2 point, int step) => new(
            Mathf.RoundToInt(point.x / step),
            Mathf.RoundToInt(point.y / step));

        public static Vector2[] ApplySteering(
            Vector2[] path,
            float steeringWeight,
            int passes,
            float jitter,
            int seed,
            Func<Vector2, bool> canTraverse)
        {
            if (path == null || path.Length < 3)
                return path ?? Array.Empty<Vector2>();

            steeringWeight = Mathf.Clamp01(steeringWeight);
            passes = Mathf.Clamp(passes, 0, 4);
            var steered = new List<Vector2>(path);
            float cornerCut = steeringWeight * 0.45f;
            for (int pass = 0; pass < passes && cornerCut > 0f; pass++)
            {
                var next = new List<Vector2>(steered.Count * 2) { steered[0] };
                for (int i = 1; i < steered.Count - 1; i++)
                {
                    Vector2 bend = steered[i];
                    Vector2 approach = Vector2.Lerp(bend, steered[i - 1], cornerCut);
                    Vector2 departure = Vector2.Lerp(bend, steered[i + 1], cornerCut);
                    next.Add(canTraverse == null || canTraverse(approach) ? approach : bend);
                    next.Add(canTraverse == null || canTraverse(departure) ? departure : bend);
                }
                next.Add(steered[^1]);
                steered = next;
            }

            if (jitter <= 0f || steered.Count < 3)
                return steered.ToArray();

            var result = new List<Vector2>(steered.Count) { steered[0] };
            for (int i = 1; i < steered.Count - 1; i++)
            {
                Vector2 tangent = (steered[i + 1] - steered[i - 1]).normalized;
                Vector2 normal = new(-tangent.y, tangent.x);
                float previousNoise = SignedHash(i / 2, seed);
                float nextNoise = SignedHash(i / 2 + 1, seed);
                float noise = Mathf.Lerp(previousNoise, nextNoise, (i & 1) * 0.5f);
                Vector2 candidate = steered[i] + normal * (noise * jitter);
                result.Add(canTraverse == null || canTraverse(candidate) ? candidate : steered[i]);
            }
            result.Add(steered[^1]);
            return result.ToArray();
        }

        private static float SignedHash(int index, int seed)
        {
            unchecked
            {
                uint value = (uint)(index * 374761393 + seed * 668265263);
                value = (value ^ (value >> 13)) * 1274126177u;
                value ^= value >> 16;
                return value / (float)uint.MaxValue * 2f - 1f;
            }
        }

        private static float Heuristic(Vector2Int a, Vector2Int b)
        {
            int dx = Mathf.Abs(a.x - b.x);
            int dy = Mathf.Abs(a.y - b.y);
            return Mathf.Max(dx, dy) + 0.41421356f * Mathf.Min(dx, dy);
        }

        private static Vector2[] Reconstruct(
            Dictionary<Vector2Int, Vector2Int> cameFrom,
            Vector2Int current,
            Vector2 start,
            Vector2 end,
            int step)
        {
            var reversed = new List<Vector2> { end };
            while (cameFrom.TryGetValue(current, out Vector2Int previous))
            {
                reversed.Add((Vector2)current * step);
                current = previous;
            }
            reversed.Add(start);
            reversed.Reverse();

            // Remove collinear grid nodes; road sampling only needs the bends.
            var result = new List<Vector2>(reversed.Count);
            for (int i = 0; i < reversed.Count; i++)
            {
                if (i > 0 && i < reversed.Count - 1)
                {
                    Vector2 before = reversed[i] - reversed[i - 1];
                    Vector2 after = reversed[i + 1] - reversed[i];
                    if (Mathf.Approximately(before.x * after.y, before.y * after.x))
                        continue;
                }
                result.Add(reversed[i]);
            }
            return result.ToArray();
        }

        private sealed class MinHeap
        {
            private readonly List<Entry> entries = new();
            public int Count => entries.Count;

            public void Push(Vector2Int node, float priority)
            {
                entries.Add(new Entry(node, priority));
                int index = entries.Count - 1;
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (entries[parent].priority <= priority) break;
                    entries[index] = entries[parent];
                    index = parent;
                }
                entries[index] = new Entry(node, priority);
            }

            public Vector2Int Pop()
            {
                Entry root = entries[0];
                Entry last = entries[^1];
                entries.RemoveAt(entries.Count - 1);
                if (entries.Count == 0) return root.node;
                int index = 0;
                while (true)
                {
                    int child = index * 2 + 1;
                    if (child >= entries.Count) break;
                    if (child + 1 < entries.Count &&
                        entries[child + 1].priority < entries[child].priority)
                        child++;
                    if (entries[child].priority >= last.priority) break;
                    entries[index] = entries[child];
                    index = child;
                }
                entries[index] = last;
                return root.node;
            }

            private readonly struct Entry
            {
                public readonly Vector2Int node;
                public readonly float priority;
                public Entry(Vector2Int node, float priority)
                {
                    this.node = node;
                    this.priority = priority;
                }
            }
        }
    }
}
