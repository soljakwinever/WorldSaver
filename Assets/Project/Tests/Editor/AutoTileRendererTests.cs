#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Project.Scripts;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;
using UnityEngine.Tilemaps;
using TileData = Project.Scripts.DataTypes.TileData;

namespace Project.Tests.EditMode
{
    public sealed class AutoTileRendererTests
    {
        private readonly List<UnityEngine.Object> _objects = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                if (_objects[i] != null)
                    UnityEngine.Object.DestroyImmediate(_objects[i]);
            }
            _objects.Clear();
        }

        [Test]
        public void DefinitionConnectsSameTileAndSharedGroups()
        {
            AutoTileDefinition firstDefinition = Create<AutoTileDefinition>();
            AutoTileDefinition secondDefinition = Create<AutoTileDefinition>();
            TileData first = Create<TileData>();
            TileData second = Create<TileData>();
            first.AutoTile = firstDefinition;
            second.AutoTile = secondDefinition;

            Assert.That(firstDefinition.Connects(first, first), Is.True);
            Assert.That(firstDefinition.Connects(first, second), Is.False);

            firstDefinition.ConnectivityGroup = "shore";
            secondDefinition.ConnectivityGroup = "shore";
            Assert.That(firstDefinition.Connects(first, second), Is.True);
        }

        [Test]
        public void GrassHeightSelectsHighRuleSpriteWhenEnabled()
        {
            Texture2D texture = Track(new Texture2D(2, 1));
            Sprite low = Track(Sprite.Create(
                texture,
                new Rect(0, 0, 1, 1),
                Vector2.one * 0.5f,
                1f));
            Sprite high = Track(Sprite.Create(
                texture,
                new Rect(1, 0, 1, 1),
                Vector2.one * 0.5f,
                1f));
            AutoTileDefinition definition = Create<AutoTileDefinition>();
            definition.Rules = new[]
            {
                new AutoTileRule
                {
                    Sprites = new[] { low },
                    GrassHeightSprites = new[]
                    {
                        new GrassHeightSpriteSet
                        {
                            Height = 0.75f,
                            Sprites = new[] { high }
                        }
                    }
                }
            };

            AutoTileResult result =
                definition.Resolve(0, Vector3Int.zero, 0.8f);

            Assert.That(((Tile)result.Tile).sprite, Is.SameAs(high));
        }

        [Test]
        public void ConnectedLiquidPoolUsesItsDominantGeneratedType()
        {
            GameObject coordinatorObject = Track(new GameObject("Liquid Resolver"));
            WorldTilemapRenderer renderer =
                coordinatorObject.AddComponent<WorldTilemapRenderer>();
            renderer.Initialize();
            Chunk chunk = CreateChunk(renderer, "Liquid Chunk", Vector2Int.zero);
            TileData water = Create<TileData>();
            water.TileId = 10;
            TileData lava = Create<TileData>();
            lava.TileId = 11;

            renderer.ApplyChunk(
                chunk,
                Vector2Int.zero,
                Array.Empty<WorldTilemapRenderer.CellData>(),
                new[]
                {
                    new WorldTilemapRenderer.CellData(
                        new Vector3Int(1, 1),
                        water,
                        Color.white),
                    new WorldTilemapRenderer.CellData(
                        new Vector3Int(2, 1),
                        lava,
                        Color.white),
                    new WorldTilemapRenderer.CellData(
                        new Vector3Int(3, 1),
                        lava,
                        Color.white)
                });

            for (int x = 1; x <= 3; x++)
            {
                Assert.That(
                    renderer.TryGetTileData(
                        PersistentTileLayer.Water,
                        new Vector3Int(x, 1),
                        out TileData liquid),
                    Is.True);
                Assert.That(liquid, Is.SameAs(lava));
            }
        }

        [Test]
        public void WallAndCeilingBakeToTheirOwnLayersWithoutGround()
        {
            GameObject coordinatorObject = Track(new GameObject("Layer Baker"));
            WorldTilemapRenderer renderer =
                coordinatorObject.AddComponent<WorldTilemapRenderer>();
            renderer.Initialize();
            Chunk chunk = CreateChunk(renderer, "Layer Chunk", Vector2Int.zero);
            TileData wall = Create<TileData>();
            wall.TileBase = Create<Tile>();
            TileData ceiling = Create<TileData>();
            ceiling.TileBase = Create<Tile>();
            Vector3Int cell = new(4, 6);

            renderer.ApplyChunk(
                chunk,
                Vector2Int.zero,
                Array.Empty<WorldTilemapRenderer.CellData>(),
                Array.Empty<WorldTilemapRenderer.CellData>(),
                new[]
                {
                    new WorldTilemapRenderer.CellData(
                        cell,
                        wall,
                        Color.white)
                },
                new[]
                {
                    new WorldTilemapRenderer.CellData(
                        cell,
                        ceiling,
                        Color.white)
                });
            renderer.CompletePendingBakesImmediately();

            Assert.That(
                chunk.GetTilemap(PersistentTileLayer.Ground).HasTile(cell),
                Is.False);
            Assert.That(
                chunk.GetTilemap(PersistentTileLayer.Wall).GetTile(cell),
                Is.SameAs(wall.TileBase));
            Assert.That(
                chunk.GetTilemap(PersistentTileLayer.Ceiling).GetTile(cell),
                Is.SameAs(ceiling.TileBase));
        }

        [Test]
        public void TransientWallSurvivesUnderlyingWorldTileChanges()
        {
            GameObject coordinatorObject =
                Track(new GameObject("Transient Wall Baker"));
            WorldTilemapRenderer renderer =
                coordinatorObject.AddComponent<WorldTilemapRenderer>();
            renderer.Initialize();
            Chunk chunk = CreateChunk(
                renderer,
                "Transient Wall Chunk",
                Vector2Int.zero);
            TileData baseline = Create<TileData>();
            baseline.TileBase = Create<Tile>();
            TileData door = Create<TileData>();
            door.TileBase = Create<Tile>();
            Vector3Int cell = new(7, 9);

            renderer.ApplyChunk(
                chunk,
                Vector2Int.zero,
                Array.Empty<WorldTilemapRenderer.CellData>(),
                Array.Empty<WorldTilemapRenderer.CellData>(),
                new[]
                {
                    new WorldTilemapRenderer.CellData(
                        cell,
                        baseline,
                        Color.white)
                });
            renderer.SetTransientWallTile(cell, door, Color.white);

            renderer.SetTile(
                PersistentTileLayer.Wall,
                cell,
                null,
                Color.white);
            renderer.CompletePendingBakesImmediately();

            Assert.That(
                renderer.TryGetTileData(
                    PersistentTileLayer.Wall,
                    cell,
                    out TileData visible),
                Is.True);
            Assert.That(visible, Is.SameAs(door));
            Assert.That(
                chunk.GetTilemap(PersistentTileLayer.Wall).GetTile(cell),
                Is.SameAs(door.TileBase));

            Assert.That(renderer.ClearTransientWallTile(cell), Is.True);
            Assert.That(
                renderer.HasTile(PersistentTileLayer.Wall, cell),
                Is.False);
            Assert.That(
                chunk.GetTilemap(PersistentTileLayer.Wall).HasTile(cell),
                Is.False);
        }

        [Test]
        public void LoadingNeighborChunkRebakesBorderFromChunkData()
        {
            GameObject coordinatorObject = Track(new GameObject("Auto Tile Baker"));
            WorldTilemapRenderer renderer =
                coordinatorObject.AddComponent<WorldTilemapRenderer>();
            renderer.Initialize();
            Chunk leftChunk = CreateChunk(renderer, "Left Chunk", Vector2Int.zero);
            Chunk rightChunk = CreateChunk(renderer, "Right Chunk", Vector2Int.right);

            Texture2D texture = Track(new Texture2D(2, 1));
            Sprite disconnected = Track(Sprite.Create(
                texture,
                new Rect(0, 0, 1, 1),
                new Vector2(0.5f, 0.5f),
                1f));
            Sprite connected = Track(Sprite.Create(
                texture,
                new Rect(1, 0, 1, 1),
                new Vector2(0.5f, 0.5f),
                1f));

            AutoTileDefinition definition = Create<AutoTileDefinition>();
            definition.DefaultSprite = disconnected;
            definition.Rules = new[]
            {
                new AutoTileRule
                {
                    RequiredNeighbors = 1 << 4, // East
                    Sprites = new[] { connected }
                }
            };
            TileData tileData = Create<TileData>();
            tileData.AutoTile = definition;

            Vector3Int leftBorder = new(31, 4, 0);
            renderer.ApplyChunk(
                leftChunk,
                Vector2Int.zero,
                new[]
                {
                    new WorldTilemapRenderer.CellData(
                        leftBorder,
                        tileData,
                        Color.white)
                },
                Array.Empty<WorldTilemapRenderer.CellData>());
            renderer.CompletePendingBakesImmediately();

            Assert.That(
                GetRenderedSprite(leftChunk, new Vector3Int(31, 4, 0)),
                Is.SameAs(disconnected));

            renderer.ApplyChunk(
                rightChunk,
                Vector2Int.right,
                new[]
                {
                    new WorldTilemapRenderer.CellData(
                        new Vector3Int(32, 4, 0),
                        tileData,
                        Color.white)
                },
                Array.Empty<WorldTilemapRenderer.CellData>());
            renderer.CompletePendingBakesImmediately();

            Assert.That(
                GetRenderedSprite(leftChunk, new Vector3Int(31, 4, 0)),
                Is.SameAs(connected));
            Assert.That(
                renderer.TryGetTileData(
                    PersistentTileLayer.Ground,
                    leftBorder,
                    out TileData logical),
                Is.True);
            Assert.That(logical, Is.SameAs(tileData));
        }

        private static Sprite GetRenderedSprite(
            Chunk chunk,
            Vector3Int localPosition)
        {
            Tilemap tilemap =
                chunk.GetTilemap(PersistentTileLayer.Ground);
            TileBase rendered = tilemap.GetTile(localPosition);
            return rendered != null
                ? tilemap.GetSprite(localPosition)
                : null;
        }

        private Chunk CreateChunk(
            WorldTilemapRenderer renderer,
            string name,
            Vector2Int position)
        {
            GameObject chunkObject = Track(new GameObject(name));
            Chunk chunk = chunkObject.AddComponent<Chunk>();
            SetPrivateField(chunk, "worldTilemapRenderer", renderer);
            chunk.Position = position;
            chunk.transform.position = new Vector3(
                position.x * ChunkBuildResult.ChunkSize,
                position.y * ChunkBuildResult.ChunkSize,
                0f);
            return chunk;
        }

        private T Create<T>() where T : ScriptableObject
        {
            return Track(ScriptableObject.CreateInstance<T>());
        }

        private T Track<T>(T value) where T : UnityEngine.Object
        {
            _objects.Add(value);
            return value;
        }

        private static void SetPrivateField(
            object target,
            string fieldName,
            object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}
#endif
