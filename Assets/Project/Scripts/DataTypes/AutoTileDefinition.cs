using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Project.Scripts.DataTypes
{
    /// <summary>
    /// Data-only auto tile definition. Unlike RuleTile, this asset is never
    /// placed on a Tilemap and does not query Tilemap state while rendering.
    /// </summary>
    [CreateAssetMenu(fileName = "New Auto Tile", menuName = "World/Auto Tile")]
    public sealed class AutoTileDefinition : ScriptableObject
    {
        [Tooltip("Definitions with the same non-empty group connect to one another. " +
                 "An empty group only connects cells using the same TileData.")]
        public string ConnectivityGroup;

        public Sprite DefaultSprite;
        public Tile.ColliderType DefaultColliderType = Tile.ColliderType.None;
        public AutoTileRule[] Rules = Array.Empty<AutoTileRule>();

        [Header("Grass Height")]
        [Tooltip("Allows grass TileData to select high variants from world noise.")]
        public bool AllowGrassHeight;
        [Tooltip("Height-keyed variants for the default sprite. The highest entry " +
                 "whose height is not greater than the sampled grass height is used.")]
        public GrassHeightSprite[] DefaultGrassHeightSprites =
            Array.Empty<GrassHeightSprite>();

        [NonSerialized] private Dictionary<TileKey, Tile> _tileCache;

        public bool Connects(TileData center, TileData neighbor)
        {
            if (center == null || neighbor == null)
                return false;
            if (ReferenceEquals(center, neighbor))
                return true;

            AutoTileDefinition other = neighbor.AutoTile;
            return other != null &&
                   !string.IsNullOrWhiteSpace(ConnectivityGroup) &&
                   string.Equals(
                       ConnectivityGroup,
                       other.ConnectivityGroup,
                       StringComparison.Ordinal);
        }

        public AutoTileResult Resolve(
            byte neighborMask,
            Vector3Int worldCell,
            float grassHeight = 0f)
        {
            AutoTileRule[] rules = Rules ?? Array.Empty<AutoTileRule>();
            for (int ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
            {
                AutoTileRule rule = rules[ruleIndex];
                if (rule == null)
                    continue;

                int transformCount = GetTransformCount(rule.RuleTransform);
                for (int transformIndex = 0; transformIndex < transformCount; transformIndex++)
                {
                    AutoTileTransform transform =
                        GetTransform(rule.RuleTransform, transformIndex);
                    byte required = TransformMask(rule.RequiredNeighbors, transform);
                    byte forbidden = TransformMask(rule.ForbiddenNeighbors, transform);
                    if ((neighborMask & required) != required ||
                        ((byte)~neighborMask & forbidden) != forbidden)
                    {
                        continue;
                    }

                    Sprite[] sprites = SelectGrassHeightSprites(
                        rule.Sprites,
                        rule.GrassHeightSprites,
                        grassHeight);
                    if (sprites == null || sprites.Length == 0)
                        break;

                    int spriteIndex = PositiveHash(
                        worldCell.x,
                        worldCell.y,
                        ruleIndex) % sprites.Length;
                    Sprite sprite = sprites[spriteIndex];
                    if (sprite == null)
                        break;

                    AutoTileTransform outputTransform = Combine(
                        transform,
                        GetRandomTransform(
                            rule.RandomTransform,
                            worldCell,
                            ruleIndex));
                    return new AutoTileResult(
                        GetOrCreateTile(sprite, rule.ColliderType),
                        ToMatrix(outputTransform));
                }
            }

            Sprite defaultSprite = SelectGrassHeightSprite(
                DefaultSprite,
                DefaultGrassHeightSprites,
                grassHeight);
            return defaultSprite != null
                ? new AutoTileResult(
                    GetOrCreateTile(defaultSprite, DefaultColliderType),
                    Matrix4x4.identity)
                : new AutoTileResult(null, Matrix4x4.identity);
        }

        private Tile GetOrCreateTile(Sprite sprite, Tile.ColliderType colliderType)
        {
            _tileCache ??= new Dictionary<TileKey, Tile>();
            TileKey key = new(sprite, colliderType);
            if (_tileCache.TryGetValue(key, out Tile tile) && tile != null)
                return tile;

            tile = CreateInstance<Tile>();
            tile.name = $"{name} (Baked {sprite.name})";
            tile.sprite = sprite;
            tile.colliderType = colliderType;
            tile.hideFlags = HideFlags.HideAndDontSave;
            _tileCache[key] = tile;
            return tile;
        }

        private static Sprite[] SelectGrassHeightSprites(
            Sprite[] fallback,
            GrassHeightSpriteSet[] variants,
            float height)
        {
            Sprite[] selected = fallback;
            float selectedHeight = float.NegativeInfinity;
            for (int i = 0; i < (variants?.Length ?? 0); i++)
            {
                GrassHeightSpriteSet variant = variants[i];
                if (variant == null ||
                    variant.Height > height ||
                    variant.Height < selectedHeight ||
                    variant.Sprites == null ||
                    variant.Sprites.Length == 0)
                {
                    continue;
                }

                selected = variant.Sprites;
                selectedHeight = variant.Height;
            }

            return selected;
        }

        private static Sprite SelectGrassHeightSprite(
            Sprite fallback,
            GrassHeightSprite[] variants,
            float height)
        {
            Sprite selected = fallback;
            float selectedHeight = float.NegativeInfinity;
            for (int i = 0; i < (variants?.Length ?? 0); i++)
            {
                GrassHeightSprite variant = variants[i];
                if (variant == null ||
                    variant.Height > height ||
                    variant.Height < selectedHeight ||
                    variant.Sprite == null)
                {
                    continue;
                }

                selected = variant.Sprite;
                selectedHeight = variant.Height;
            }

            return selected;
        }

        private static int GetTransformCount(AutoTileRuleTransform transform)
        {
            return transform switch
            {
                AutoTileRuleTransform.Rotated => 4,
                AutoTileRuleTransform.MirrorX => 2,
                AutoTileRuleTransform.MirrorY => 2,
                AutoTileRuleTransform.RotatedAndMirrored => 8,
                _ => 1
            };
        }

        private static AutoTileTransform GetTransform(
            AutoTileRuleTransform allowed,
            int index)
        {
            return allowed switch
            {
                AutoTileRuleTransform.Rotated =>
                    new AutoTileTransform(index, false),
                AutoTileRuleTransform.MirrorX =>
                    new AutoTileTransform(0, index == 1),
                AutoTileRuleTransform.MirrorY =>
                    index == 0
                        ? AutoTileTransform.Identity
                        : new AutoTileTransform(2, true),
                AutoTileRuleTransform.RotatedAndMirrored =>
                    new AutoTileTransform(index & 3, index >= 4),
                _ => AutoTileTransform.Identity
            };
        }

        private static AutoTileTransform GetRandomTransform(
            AutoTileRandomTransform randomTransform,
            Vector3Int cell,
            int salt)
        {
            int hash = PositiveHash(cell.x, cell.y, salt + 7919);
            return randomTransform switch
            {
                AutoTileRandomTransform.Rotated =>
                    new AutoTileTransform(hash & 3, false),
                AutoTileRandomTransform.MirrorX =>
                    new AutoTileTransform(0, (hash & 1) != 0),
                AutoTileRandomTransform.MirrorY =>
                    (hash & 1) == 0
                        ? AutoTileTransform.Identity
                        : new AutoTileTransform(2, true),
                _ => AutoTileTransform.Identity
            };
        }

        private static AutoTileTransform Combine(
            AutoTileTransform first,
            AutoTileTransform second)
        {
            // Dihedral composition: rotate first, then optionally mirror.
            int rotations = second.Mirrored
                ? second.QuarterTurns - first.QuarterTurns
                : second.QuarterTurns + first.QuarterTurns;
            return new AutoTileTransform(
                rotations,
                first.Mirrored ^ second.Mirrored);
        }

        private static byte TransformMask(
            byte source,
            AutoTileTransform transform)
        {
            byte result = 0;
            for (int bit = 0; bit < AutoTileDirections.Count; bit++)
            {
                if ((source & (1 << bit)) == 0)
                    continue;

                Vector2Int direction = AutoTileDirections.Get(bit);
                if (transform.Mirrored)
                    direction.x = -direction.x;
                for (int turn = 0; turn < transform.QuarterTurns; turn++)
                    direction = new Vector2Int(-direction.y, direction.x);

                result |= (byte)(1 << AutoTileDirections.IndexOf(direction));
            }

            return result;
        }

        private static Matrix4x4 ToMatrix(AutoTileTransform transform)
        {
            Matrix4x4 rotation = Matrix4x4.Rotate(
                Quaternion.Euler(0f, 0f, transform.QuarterTurns * 90f));
            if (!transform.Mirrored)
                return rotation;

            return rotation * Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
        }

        private static int PositiveHash(int x, int y, int salt)
        {
            unchecked
            {
                // Mix both coordinates with unrelated large odd constants,
                // then avalanche every bit. This avoids the low-bit patterns
                // produced by taking a small modulo of a plain FNV hash.
                uint hash = (uint)x * 0x8da6b343u;
                hash ^= (uint)y * 0xd8163841u;
                hash ^= (uint)salt * 0xcb1ab31fu;
                hash ^= 0x9e3779b9u;
                hash ^= hash >> 16;
                hash *= 0x7feb352du;
                hash ^= hash >> 15;
                hash *= 0x846ca68bu;
                hash ^= hash >> 16;
                return (int)(hash & 0x7fffffffu);
            }
        }

        private readonly struct TileKey : IEquatable<TileKey>
        {
            private readonly Sprite _sprite;
            private readonly Tile.ColliderType _colliderType;

            public TileKey(Sprite sprite, Tile.ColliderType colliderType)
            {
                _sprite = sprite;
                _colliderType = colliderType;
            }

            public bool Equals(TileKey other)
            {
                return _sprite == other._sprite &&
                       _colliderType == other._colliderType;
            }

            public override bool Equals(object obj)
            {
                return obj is TileKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(_sprite, (int)_colliderType);
            }
        }

        private readonly struct AutoTileTransform
        {
            public static readonly AutoTileTransform Identity = new(0, false);

            public readonly int QuarterTurns;
            public readonly bool Mirrored;

            public AutoTileTransform(int quarterTurns, bool mirrored)
            {
                QuarterTurns = ((quarterTurns % 4) + 4) % 4;
                Mirrored = mirrored;
            }
        }
    }

    [Serializable]
    public sealed class AutoTileRule
    {
        [Tooltip("Bits that must connect. Bit order: NW, N, NE, W, E, SW, S, SE.")]
        public byte RequiredNeighbors;

        [Tooltip("Bits that must not connect. Bit order: NW, N, NE, W, E, SW, S, SE.")]
        public byte ForbiddenNeighbors;

        public Sprite[] Sprites = Array.Empty<Sprite>();
        [Tooltip("Height-keyed sprite sets for this rule. The normal sprites are " +
                 "used below the first configured height.")]
        public GrassHeightSpriteSet[] GrassHeightSprites =
            Array.Empty<GrassHeightSpriteSet>();
        public Tile.ColliderType ColliderType = Tile.ColliderType.None;
        public AutoTileRuleTransform RuleTransform;
        public AutoTileRandomTransform RandomTransform;
    }

    [Serializable]
    public sealed class GrassHeightSprite
    {
        [Range(0f, 1f)] public float Height;
        public Sprite Sprite;
    }

    [Serializable]
    public sealed class GrassHeightSpriteSet
    {
        [Range(0f, 1f)] public float Height;
        public Sprite[] Sprites = Array.Empty<Sprite>();
    }

    public enum AutoTileRuleTransform : byte
    {
        Fixed,
        Rotated,
        MirrorX,
        MirrorY,
        RotatedAndMirrored
    }

    public enum AutoTileRandomTransform : byte
    {
        Fixed,
        Rotated,
        MirrorX,
        MirrorY
    }

    public readonly struct AutoTileResult
    {
        public readonly TileBase Tile;
        public readonly Matrix4x4 Transform;

        public AutoTileResult(TileBase tile, Matrix4x4 transform)
        {
            Tile = tile;
            Transform = transform;
        }
    }

    public static class AutoTileDirections
    {
        public const int Count = 8;

        private static readonly Vector2Int[] Values =
        {
            new(-1, 1),
            new(0, 1),
            new(1, 1),
            new(-1, 0),
            new(1, 0),
            new(-1, -1),
            new(0, -1),
            new(1, -1)
        };

        public static Vector2Int Get(int index)
        {
            return Values[index];
        }

        public static int IndexOf(Vector2Int direction)
        {
            for (int i = 0; i < Values.Length; i++)
            {
                if (Values[i] == direction)
                    return i;
            }

            throw new ArgumentOutOfRangeException(nameof(direction));
        }
    }
}
