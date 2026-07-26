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
    }

    /// <summary>Tool used by <c>ToolHotbarAction</c>.</summary>
    [Serializable]
    public sealed class ToolHotbarActionData : ItemActionData
    {
        public ToolData tool;
    }

    /// <summary>Per-item mining rules used by <c>MineTileToolAction</c>.</summary>
    [Serializable]
    public sealed class MineTileToolActionData : ItemActionData
    {
        public PersistentTileLayer layer = PersistentTileLayer.Ground;

        [Tooltip("Entity tags this item can mine. Leave empty to allow any tile.")]
        public EntityTag[] mineableTags = Array.Empty<EntityTag>();
    }

    /// <summary>Node placed by <c>PlacePersistentNodeItemAction</c>.</summary>
    [Serializable]
    public sealed class PlacePersistentNodeItemActionData : ItemActionData
    {
        public NodeData node;
    }
}
