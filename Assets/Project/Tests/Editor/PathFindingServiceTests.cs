#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Project.Scripts.Interface;
using Project.Scripts.Pathfinding;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class PathFindingServiceTests
    {
        [Test]
        public void FindsPathAcrossPositiveAndNegativeChunkBoundaries()
        {
            TestMap map = new();
            PathFindingService service = new(map);
            List<Vector2Int> path = new();

            bool found = service.TryFindPath(
                new Vector2Int(-33, 0),
                new Vector2Int(33, 0),
                path);

            Assert.That(found, Is.True);
            Assert.That(path[0], Is.EqualTo(new Vector2Int(-33, 0)));
            Assert.That(path[path.Count - 1], Is.EqualTo(new Vector2Int(33, 0)));
            Assert.That(path, Has.Count.EqualTo(67));
            Assert.That(map.Queries, Is.GreaterThan(0));
        }

        [Test]
        public void RoutesAroundBlockedTiles()
        {
            TestMap map = new();
            map.Blocked.UnionWith(new[]
            {
                new Vector2Int(1, -1),
                new Vector2Int(1, 0),
                new Vector2Int(1, 1)
            });
            PathFindingService service = new(map);
            List<Vector2Int> path = new();

            bool found = service.TryFindPath(Vector2Int.zero, new Vector2Int(2, 0), path);

            Assert.That(found, Is.True);
            Assert.That(path, Has.None.EqualTo(new Vector2Int(1, 0)));
            Assert.That(path.Count, Is.EqualTo(7));
        }

        [Test]
        public void ChoosesLowerTraversalCost()
        {
            TestMap map = new();
            map.Costs[new Vector2Int(1, 0)] = 100f;
            PathFindingService service = new(map);
            List<Vector2Int> path = new();

            Assert.That(
                service.TryFindPath(Vector2Int.zero, new Vector2Int(2, 0), path),
                Is.True);
            Assert.That(path, Has.None.EqualTo(new Vector2Int(1, 0)));
        }

        [Test]
        public void CachedCellCanBeInvalidatedAfterWorldEdit()
        {
            TestMap map = new();
            PathFindingService service = new(map);
            List<Vector2Int> path = new();
            Vector2Int destination = new(1, 0);

            Assert.That(service.TryFindPath(Vector2Int.zero, destination, path), Is.True);
            map.Blocked.Add(destination);
            Assert.That(service.TryFindPath(Vector2Int.zero, destination, path), Is.True);

            service.Invalidate(destination);

            Assert.That(service.TryFindPath(Vector2Int.zero, destination, path), Is.False);
            Assert.That(path, Is.Empty);
        }

        [Test]
        public void RespectsVisitBudgetWhenDestinationIsUnreachable()
        {
            TestMap map = new();
            map.Blocked.UnionWith(new[]
            {
                new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
                new Vector2Int(-1, 0),                            new Vector2Int(1, 0),
                new Vector2Int(-1, 1),  new Vector2Int(0, 1),  new Vector2Int(1, 1)
            });
            PathFindingService service = new(map);
            List<Vector2Int> path = new();

            Assert.That(
                service.TryFindPath(Vector2Int.zero, new Vector2Int(10, 10), path, 25),
                Is.False);
            Assert.That(path, Is.Empty);
        }

        [Test]
        public async Task FindsPathAgainstWorkerSafeLocalSnapshot()
        {
            LocalTestMap map = new();
            PathFindingService service = new(map);

            List<Vector2Int> path = await service.FindPathAsync(
                new Vector2Int(-4, 2),
                new Vector2Int(4, 2),
                100);

            Assert.That(map.SnapshotRequests, Is.EqualTo(1));
            Assert.That(path, Is.Not.Null);
            Assert.That(path[0], Is.EqualTo(new Vector2Int(-4, 2)));
            Assert.That(path[path.Count - 1], Is.EqualTo(new Vector2Int(4, 2)));
        }

        [Test]
        public async Task PassesAgentContextIntoLocalSnapshot()
        {
            ContextualTestMap map = new();
            PathFindingService service = new(map);
            PathFindingQuery query = new(
                "actor",
                "village",
                "faction",
                new[] { "fire" });

            List<Vector2Int> path = await service.FindPathAsync(
                Vector2Int.zero,
                new Vector2Int(2, 0),
                query,
                100);

            Assert.That(path, Is.Not.Null);
            Assert.That(map.Captured.ActorId, Is.EqualTo("actor"));
            Assert.That(map.Captured.HasImmunity("fire"), Is.True);
        }

        private sealed class TestMap : IPathFindingMap
        {
            public readonly HashSet<Vector2Int> Blocked = new();
            public readonly Dictionary<Vector2Int, float> Costs = new();
            public int Queries { get; private set; }

            public bool IsWalkable(Vector2Int worldCell)
            {
                Queries++;
                return !Blocked.Contains(worldCell);
            }

            public float GetTraversalCost(Vector2Int worldCell) =>
                Costs.TryGetValue(worldCell, out float cost) ? cost : 1f;
        }

        private sealed class LocalTestMap :
            IPathFindingMap,
            ILocalPathFindingMap
        {
            private readonly TestMap _snapshot = new();

            public int SnapshotRequests { get; private set; }

            public bool TryCreateLocalSnapshot(
                Vector2Int start,
                Vector2Int destination,
                out IPathFindingMap snapshot)
            {
                SnapshotRequests++;
                snapshot = _snapshot;
                return true;
            }

            public bool IsWalkable(Vector2Int worldCell) =>
                throw new AssertionException(
                    "The generated-world fallback must not be used for a local async search.");

            public float GetTraversalCost(Vector2Int worldCell) =>
                throw new AssertionException(
                    "The generated-world fallback must not be used for a local async search.");
        }

        private sealed class ContextualTestMap :
            IPathFindingMap,
            IContextualLocalPathFindingMap
        {
            private readonly TestMap _snapshot = new();
            public PathFindingQuery Captured { get; private set; }

            public bool TryCreateLocalSnapshot(
                Vector2Int start,
                Vector2Int destination,
                PathFindingQuery query,
                out IPathFindingMap snapshot)
            {
                Captured = query;
                snapshot = _snapshot;
                return true;
            }

            public bool IsWalkable(Vector2Int worldCell) => true;
            public float GetTraversalCost(Vector2Int worldCell) => 1f;
        }
    }
}
#endif
