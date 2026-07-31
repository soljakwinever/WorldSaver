using NUnit.Framework;
using Project.Scripts.Core;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class TileReservationSystemTests
    {
        [Test]
        public void Reservation_BlocksEveryCellInArea_UntilReleased()
        {
            object owner = new();
            RectInt area = new(41000, 42000, 3, 2);

            Assert.That(
                TileReservationSystem.TryReserve(owner, area),
                Is.True);
            Assert.That(
                TileReservationSystem.IsReserved(
                    new Vector2Int(41000, 42000)),
                Is.True);
            Assert.That(
                TileReservationSystem.IsReserved(
                    new Vector2Int(41002, 42001)),
                Is.True);
            Assert.That(
                TileReservationSystem.IsReserved(
                    new Vector2Int(41003, 42001)),
                Is.False);

            TileReservationSystem.Release(owner);

            Assert.That(
                TileReservationSystem.IsReserved(
                    new Vector2Int(41000, 42000)),
                Is.False);
            Assert.That(
                TileReservationSystem.IsReserved(
                    new Vector2Int(41002, 42001)),
                Is.False);
        }

        [Test]
        public void OverlappingReservation_IsRejectedWithoutChangingOwner()
        {
            object firstOwner = new();
            object secondOwner = new();
            RectInt firstArea = new(43000, 44000, 2, 2);

            try
            {
                Assert.That(
                    TileReservationSystem.TryReserve(
                        firstOwner,
                        firstArea),
                    Is.True);
                Assert.That(
                    TileReservationSystem.TryReserve(
                        secondOwner,
                        new RectInt(43001, 44001, 2, 2)),
                    Is.False);
                Assert.That(
                    TileReservationSystem.IsReserved(
                        new Vector2Int(43001, 44001),
                        firstOwner),
                    Is.False);
            }
            finally
            {
                TileReservationSystem.Release(firstOwner);
                TileReservationSystem.Release(secondOwner);
            }
        }

        [Test]
        public void Owner_CanMoveItsReservationAtomically()
        {
            object owner = new();
            Vector2Int oldCell = new(45000, 46000);
            Vector2Int newCell = new(45001, 46000);

            try
            {
                Assert.That(
                    TileReservationSystem.TryReserve(
                        owner,
                        new RectInt(oldCell, Vector2Int.one)),
                    Is.True);
                Assert.That(
                    TileReservationSystem.TryReserve(
                        owner,
                        new RectInt(newCell, Vector2Int.one)),
                    Is.True);
                Assert.That(
                    TileReservationSystem.IsReserved(oldCell),
                    Is.False);
                Assert.That(
                    TileReservationSystem.IsReserved(newCell),
                    Is.True);
            }
            finally
            {
                TileReservationSystem.Release(owner);
            }
        }
    }
}
