using System;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "New Tile Data", menuName = "World/Tile Data")]
    public sealed class TileData : ScriptableObject
    {
        public enum AiPathingTerrain : byte
        {
            Normal,
            Difficult,
            Hazard
        }

        [Flags]
        public enum TileFlags : byte
        {
            None = 0,
            Grass = 1 << 0,
            EnclosesRoom = 1 << 1
        }

        [Min(0)] public int TileId;
        public TileBase TileBase;
        [Tooltip("Optional custom auto-tile definition. When assigned, the renderer " +
                 "bakes a static Tile from chunk neighbor data instead of placing TileBase.")]
        public AutoTileDefinition AutoTile;
        public Color Color = Color.white;
        [Tooltip("Intrinsic tile traits used by generation and rendering.")]
        public TileFlags Flags;
        [Tooltip("Places this tile on the wall layer and leaves its ground cell empty.")]
        public bool IsWall;
        [Tooltip("Optional tile placed on the ceiling layer while this wall exists.")]
        public TileData ceilingTile;

        [Header("AI Pathing")]
        [Tooltip("Additional traversal hint used by AI path searches.")]
        public AiPathingTerrain pathingTerrain;
        [Min(1f)]
        [Tooltip("Movement/path cost multiplier. Difficult terrain should use a value above 1.")]
        public float pathingCost = 1f;
        [Tooltip("Stable immunity ID required to safely cross a hazard. Non-immune AI treats the tile as blocked.")]
        public string hazardImmunityId;

        [Header("Wall Durability")]
        [Tooltip("Baseline hit points for this wall. Runtime HP is persisted only after it changes.")]
        public byte wallHealth = 100;
        [Tooltip("Damage reduction applied when this wall is hit. A successful mining hit always deals at least 1 damage.")]
        public byte hardness;
        [Tooltip("Optional replacement used when this wall is destroyed by direct damage.")]
        public TileData destroyedTile;
        [Tooltip("Optional replacement used when this wall burns down.")]
        public TileData burnedTile;

        public bool HasVisual => AutoTile != null || TileBase != null;
        public bool IsGrass => (Flags & TileFlags.Grass) != 0;
        public bool EnclosesRoom => (Flags & TileFlags.EnclosesRoom) != 0;

        [Header("Mining Drop")]
        [Tooltip("Item dropped when this tile is successfully mined.")]
        public ItemData droppedItem;

        [Range(0f, 1f)]
        [Tooltip("Chance that mining this tile drops its associated item.")]
        public float dropChance = 1f;

        [SerializeField]
        private EntityTag[] tags = Array.Empty<EntityTag>();

        [SerializeField]
        private ValueTagAssignment[] valueTags =
            Array.Empty<ValueTagAssignment>();

        public bool HasTag(EntityTag tag)
        {
            if (tag == null)
                return false;

            for (int i = 0; i < (tags?.Length ?? 0); i++)
            {
                if (tags[i] == tag)
                    return true;
            }

            return ValueTagLookup.HasTag(valueTags, tag);
        }

        public bool TryGetValue(ValueTag tag, out int value) =>
            ValueTagLookup.TryGetInt(valueTags, tag, out value);

        public bool TryGetValue(ValueTag tag, out float value) =>
            ValueTagLookup.TryGetFloat(valueTags, tag, out value);
    }
}
