using System;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    /// <summary>
    /// Base for polymorphic data stored inline on an <see cref="ItemData"/>.
    /// Add one record for each item or tool action that needs configuration.
    /// </summary>
    [Serializable]
    public abstract class ItemActionData
    {
    }

    /// <summary>Tile and layer used by <c>PlaceTileItemAction</c>.</summary>
    [Serializable]
    public sealed class PlaceTileItemActionData : ItemActionData
    {
        public TileData tile;
        public PersistentTileLayer layer = PersistentTileLayer.Ground;
        [Min(0.01f)] public float constructionWorkRequired = 1f;
    }

    /// <summary>Tool used by <c>ToolHotbarAction</c>.</summary>
    [Serializable]
    public sealed class ToolHotbarActionData : ItemActionData
    {
        public ToolData tool;
    }

    /// <summary>Skill and presentation supplied by a usable item.</summary>
    [Serializable]
    public sealed class UseSkillItemActionData : ItemActionData
    {
        public SkillData skill;
        [Tooltip("Optional projectile supplied to projectile skill actions.")]
        public ProjectileData projectile;
        [Tooltip("Overrides the sprite on weapon swing animations used by this skill. Leave empty to use the animation asset's sprite.")]
        public Sprite weaponSpriteOverride;
    }

    /// <summary>Needs restored by <c>IncreaseNeedsItemAction</c>.</summary>
    [Serializable]
    public sealed class IncreaseNeedsActionData : ItemActionData
    {
        [Min(0f)]
        [Tooltip("Normalized Hunger restored per use, where 1 fills the bar.")]
        public float hunger;

        [Min(0f)]
        [Tooltip("Normalized Energy restored per use, where 1 fills the bar.")]
        public float energy;
    }

    /// <summary>
    /// Health and mana restored by <c>IncreaseHealthManaItemAction</c>.
    /// </summary>
    [Serializable]
    public sealed class IncreaseHealthManaActionData : ItemActionData
    {
        [Min(0)]
        [Tooltip("Health points restored per use.")]
        public int health;

        [Min(0)]
        [Tooltip("Mana points restored per use.")]
        public int mana;
    }

    /// <summary>Per-item mining rules used by <c>MineTileToolAction</c>.</summary>
    [Serializable]
    public sealed class MineTileToolActionData : ItemActionData
    {
        public PersistentTileLayer layer = PersistentTileLayer.Ground;

        [Tooltip("Entity tags this item can mine. Leave empty to allow any tile.")]
        public EntityTag[] mineableTags = Array.Empty<EntityTag>();
    }

    /// <summary>Turns a tagged tile into another registered tile.</summary>
    [Serializable]
    public sealed class TransformTaggedTileToolActionData : ItemActionData
    {
        public PersistentTileLayer layer = PersistentTileLayer.Ground;
        [Tooltip("Only tiles carrying this tag can be transformed.")]
        public EntityTag requiredTag;
        public TileData replacementTile;
    }

    [Serializable]
    public sealed class WaterTileToolActionData : ItemActionData
    {
        [Min(1), Tooltip("Durability (stored water) removed from the watering can per use, regardless of how many tiles are watered.")]
        public byte waterPerUse = 1;

        [Min(0f), Tooltip("Radius in tiles around the targeted tile that will be fully saturated. Zero waters only the targeted tile.")]
        public float wateringRadius;
    }

    /// <summary>
    /// Per-item coverage removal used by <c>MineCoverageToolAction</c>.
    /// </summary>
    [Serializable]
    public sealed class MineCoverageToolActionData : ItemActionData
    {
        [Min(0f)]
        [Tooltip("Normalized coverage removed from the targeted tile per use.")]
        public float amount = 0.25f;
    }

    /// <summary>Node placed by <c>PlacePersistentNodeItemAction</c>.</summary>
    [Serializable]
    public sealed class PlacePersistentNodeItemActionData : ItemActionData
    {
        public NodeData node;
        [Tooltip("Require every cell in the configured area to be walkable before placement.")]
        public bool requireWalkableArea;
        public Vector2Int walkableAreaSize = Vector2Int.one;
        public Vector2Int walkableAreaOffset;
        [Min(0.01f)] public float constructionWorkRequired = 1f;
    }

    [Serializable]
    public sealed class WaterPlantItemActionData : ItemActionData
    {
        [Min(0.01f)] public float waterPoints = 1f;
    }

    /// <summary>
    /// Converts one selected item into another while the player directly
    /// interacts from a matching world tile.
    /// </summary>
    [Serializable]
    public sealed class ConvertItemOnTileActionData : ItemActionData
    {
        [Tooltip("Layer checked on the cell occupied by the player.")]
        public PersistentTileLayer layer = PersistentTileLayer.Water;

        [Tooltip("The tile required on that layer.")]
        public TileData requiredTile;

        [Tooltip("Item added after one of the selected items is removed.")]
        public ItemData replacementItem;
    }

    [Serializable]
    public sealed class FastTravelPortalActionData : ItemActionData
    {
        public GameObject portalPrefab;
        public RenderTexture renderTexture;
        public GameObject startOneShot;
        public GameObject readyOneShot;
        [Tooltip("Persistent visible particles around the portal. These do not write to the destination stencil.")]
        public GameObject outerRimEffect;
        [Min(0.1f)] public float creationDuration = 3f;
        [Min(0.1f), Tooltip("Multiplier applied to the portal's authored particle sizes.")]
        public float particleSizeMultiplier = 6f;
        [Min(32)] public int textureSize = 256;
        [Min(0.1f)] public float activationDistance = 1.5f;
        [Min(0.05f)] public float transitionDuration = 0.45f;
        public Vector2 entranceOffset = new(2f, 0f);
    }
}
