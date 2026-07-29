#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using Project.Scripts;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class RoomFloodFillTests
    {
        [Test]
        public void DetectsCardinallyEnclosedInterior()
        {
            HashSet<Vector3Int> boundary = CreateRectangleBoundary(0, 0, 4, 4);
            RoomFloodFill fill = new();

            RoomFloodResult result = fill.Evaluate(
                new Vector3Int(2, 2),
                100,
                cell => Classify(cell, boundary, -1, -1, 5, 5));

            Assert.That(result.Status, Is.EqualTo(RoomFloodStatus.Enclosed));
            Assert.That(result.Interior.Count, Is.EqualTo(9));
            Assert.That(result.Boundary.Count, Is.EqualTo(12));
        }

        [Test]
        public void BoundaryDoorRemainsEnclosingRegardlessOfMovementState()
        {
            HashSet<Vector3Int> boundary = CreateRectangleBoundary(0, 0, 4, 4);
            Vector3Int door = new(2, 0);
            Assert.That(boundary.Contains(door), Is.True);

            RoomFloodResult result = new RoomFloodFill().Evaluate(
                new Vector3Int(2, 2),
                100,
                cell => Classify(cell, boundary, -1, -1, 5, 5));

            Assert.That(result.Status, Is.EqualTo(RoomFloodStatus.Enclosed));
            Assert.That(result.Boundary, Does.Contain(door));
        }

        [Test]
        public void MissingBoundaryOpeningReachesUnavailableWorld()
        {
            HashSet<Vector3Int> boundary = CreateRectangleBoundary(0, 0, 4, 4);
            boundary.Remove(new Vector3Int(2, 0));

            RoomFloodResult result = new RoomFloodFill().Evaluate(
                new Vector3Int(2, 2),
                100,
                cell => Classify(cell, boundary, -1, -1, 5, 5));

            Assert.That(result.Status, Is.EqualTo(RoomFloodStatus.Open));
        }

        [Test]
        public void UnavailableNeighborRejectsOtherwiseClosedCandidate()
        {
            HashSet<Vector3Int> boundary = CreateRectangleBoundary(0, 0, 4, 4);
            boundary.Remove(new Vector3Int(4, 2));

            RoomFloodResult result = new RoomFloodFill().Evaluate(
                new Vector3Int(2, 2),
                100,
                cell => cell == new Vector3Int(4, 2)
                    ? RoomCellKind.Unavailable
                    : Classify(cell, boundary, -1, -1, 5, 5));

            Assert.That(result.Status, Is.EqualTo(RoomFloodStatus.Open));
        }

        [Test]
        public void OversizedCandidateStopsAtConfiguredLimit()
        {
            HashSet<Vector3Int> boundary = CreateRectangleBoundary(0, 0, 6, 6);

            RoomFloodResult result = new RoomFloodFill().Evaluate(
                new Vector3Int(3, 3),
                10,
                cell => Classify(cell, boundary, -1, -1, 7, 7));

            Assert.That(result.Status, Is.EqualTo(RoomFloodStatus.TooLarge));
            Assert.That(result.Interior.Count, Is.EqualTo(11));
        }

        [Test]
        public void InternalDividerSplitsAndRemovalMergesRooms()
        {
            HashSet<Vector3Int> boundary = CreateRectangleBoundary(0, 0, 6, 6);
            for (int y = 1; y < 6; y++)
                boundary.Add(new Vector3Int(3, y));

            RoomFloodFill fill = new();
            RoomFloodResult left = fill.Evaluate(
                new Vector3Int(1, 1),
                100,
                cell => Classify(cell, boundary, -1, -1, 7, 7));
            Assert.That(left.Status, Is.EqualTo(RoomFloodStatus.Enclosed));
            Assert.That(left.Interior.Count, Is.EqualTo(10));

            for (int y = 1; y < 6; y++)
                boundary.Remove(new Vector3Int(3, y));
            RoomFloodResult merged = fill.Evaluate(
                new Vector3Int(1, 1),
                100,
                cell => Classify(cell, boundary, -1, -1, 7, 7));

            Assert.That(merged.Status, Is.EqualTo(RoomFloodStatus.Enclosed));
            Assert.That(merged.Interior.Count, Is.EqualTo(25));
        }

        [Test]
        public void SupportsNegativeWorldCoordinates()
        {
            HashSet<Vector3Int> boundary =
                CreateRectangleBoundary(-4, -4, 0, 0);

            RoomFloodResult result = new RoomFloodFill().Evaluate(
                new Vector3Int(-2, -2),
                100,
                cell => Classify(cell, boundary, -5, -5, 1, 1));

            Assert.That(result.Status, Is.EqualTo(RoomFloodStatus.Enclosed));
            Assert.That(result.Interior.Count, Is.EqualTo(9));
        }

        [Test]
        public void RoofPaddingAddsAnAdditionalUpwardRowAndDeduplicatesOverlap()
        {
            List<Vector3Int> source = new()
            {
                new Vector3Int(0, 0),
                new Vector3Int(1, 0)
            };
            HashSet<Vector3Int> padded = new();

            RoomRoofPadding.ExpandRoofAndMask(source, padded);

            Assert.That(padded.Count, Is.EqualTo(16));
            Assert.That(padded, Does.Contain(new Vector3Int(-1, -1)));
            Assert.That(padded, Does.Contain(new Vector3Int(2, 2)));
            Assert.That(
                padded.Contains(new Vector3Int(0, -2)),
                Is.False);
            Assert.That(
                padded.Contains(new Vector3Int(0, 3)),
                Is.False);
        }

        [Test]
        public void VisibilityMaskLeavesRoomCellsUncovered()
        {
            HashSet<Vector3Int> visible = new()
            {
                new Vector3Int(1, 1)
            };
            List<Vector3> vertices = new();
            List<int> triangles = new();

            int quads = RoomVisibilityMaskBuilder.Build(
                new BoundsInt(0, 0, 0, 3, 3, 1),
                visible,
                vertices,
                triangles);

            Assert.That(quads, Is.EqualTo(8));
            Assert.That(vertices.Count, Is.EqualTo(32));
            Assert.That(triangles.Count, Is.EqualTo(48));
        }

        private static RoomCellKind Classify(
            Vector3Int cell,
            HashSet<Vector3Int> boundary,
            int minimumX,
            int minimumY,
            int maximumX,
            int maximumY)
        {
            if (boundary.Contains(cell))
                return RoomCellKind.Boundary;
            return cell.x < minimumX ||
                   cell.y < minimumY ||
                   cell.x > maximumX ||
                   cell.y > maximumY
                ? RoomCellKind.Unavailable
                : RoomCellKind.Open;
        }

        private static HashSet<Vector3Int> CreateRectangleBoundary(
            int minimumX,
            int minimumY,
            int maximumX,
            int maximumY)
        {
            HashSet<Vector3Int> result = new();
            for (int x = minimumX; x <= maximumX; x++)
            {
                result.Add(new Vector3Int(x, minimumY));
                result.Add(new Vector3Int(x, maximumY));
            }
            for (int y = minimumY; y <= maximumY; y++)
            {
                result.Add(new Vector3Int(minimumX, y));
                result.Add(new Vector3Int(maximumX, y));
            }
            return result;
        }
    }
}
#endif
