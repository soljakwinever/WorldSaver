using System.Reflection;
using NUnit.Framework;
using Project.Scripts.DataTypes;
using Project.Scripts.Persistence;
using UnityEngine;

namespace Project.Tests.Editor
{
    public sealed class PlaneTransitionTests
    {
        private PlaneData _surface;
        private PlaneData _underground;

        [SetUp]
        public void SetUp()
        {
            _surface = CreatePlane("surface");
            _underground = CreatePlane("underground");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_surface);
            Object.DestroyImmediate(_underground);
        }

        [Test]
        public void PlaneSelection_NotifiesExactlyOnceWhenPlaneChanges()
        {
            PlaneSelection selection = new(_surface);
            int notifications = 0;
            PlaneData previous = null;
            PlaneData current = null;
            selection.Changed += (oldPlane, newPlane) =>
            {
                notifications++;
                previous = oldPlane;
                current = newPlane;
            };

            selection.SetPlane(_underground);
            selection.SetPlane(_underground);

            Assert.That(selection.Plane, Is.SameAs(_underground));
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(previous, Is.SameAs(_surface));
            Assert.That(current, Is.SameAs(_underground));
        }

        [Test]
        public void RegionStore_UsesCurrentPlaneNamespace()
        {
            PlaneSelection selection = new(_surface);
            FileRegionDiskStore store = new(selection);

            StringAssert.Contains(
                System.IO.Path.Combine("planes", "surface", "regions"),
                store.RegionDirectory);

            selection.SetPlane(_underground);

            StringAssert.Contains(
                System.IO.Path.Combine("planes", "underground", "regions"),
                store.RegionDirectory);
        }

        private static PlaneData CreatePlane(string id)
        {
            PlaneData plane = ScriptableObject.CreateInstance<PlaneData>();
            typeof(PlaneData)
                .GetField("persistentId", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(plane, id);
            return plane;
        }
    }
}
