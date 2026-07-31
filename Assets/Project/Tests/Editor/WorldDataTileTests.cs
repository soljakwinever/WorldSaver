#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Project.Scripts;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class WorldDataTileTests
    {
        [Test]
        public void ExplicitTileIdSurvivesRegistryReordering()
        {
            WorldData world = ScriptableObject.CreateInstance<WorldData>();
            TileData first = ScriptableObject.CreateInstance<TileData>();
            TileData second = ScriptableObject.CreateInstance<TileData>();
            UnityEngine.Tilemaps.Tile tileBase =
                ScriptableObject.CreateInstance<UnityEngine.Tilemaps.Tile>();

            try
            {
                first.TileId = 42;
                first.TileBase = tileBase;
                second.TileId = 7;
                second.TileBase = tileBase;
                world.tiles = new[] { second, first };

                Assert.That(world.TryGetTileData(42, out TileData restored), Is.True);
                Assert.That(restored, Is.SameAs(first));

                world.tiles = new[] { first, second };
                Assert.That(world.TryGetTileData(42, out restored), Is.True);
                Assert.That(restored, Is.SameAs(first));
            }
            finally
            {
                Object.DestroyImmediate(tileBase);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(world);
            }
        }

        [Test]
        public void UnknownTileIdDoesNotResolve()
        {
            WorldData world = ScriptableObject.CreateInstance<WorldData>();
            try
            {
                world.tiles = System.Array.Empty<TileData>();
                Assert.That(world.TryGetTileData(99, out TileData tile), Is.False);
                Assert.That(tile, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(world);
            }
        }

        [Test]
        public void DuplicateTileIdDoesNotResolve()
        {
            WorldData world = ScriptableObject.CreateInstance<WorldData>();
            TileData first = ScriptableObject.CreateInstance<TileData>();
            TileData second = ScriptableObject.CreateInstance<TileData>();
            UnityEngine.Tilemaps.Tile tileBase =
                ScriptableObject.CreateInstance<UnityEngine.Tilemaps.Tile>();

            try
            {
                first.TileId = 5;
                first.TileBase = tileBase;
                second.TileId = 5;
                second.TileBase = tileBase;
                world.tiles = new[] { first, second };

                Assert.That(world.TryGetTileData(5, out TileData tile), Is.False);
                Assert.That(tile, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(tileBase);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(world);
            }
        }
    }
}
#endif
