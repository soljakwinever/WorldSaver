using System;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "New Tile Data", menuName = "World/Tile Data")]
    public sealed class TileData : ScriptableObject
    {
        [Min(0)] public int TileId;
        public TileBase TileBase;
        public Color Color = Color.white;

        [Header("Mining Drop")]
        [Tooltip("Item dropped when this tile is successfully mined.")]
        public ItemData droppedItem;

        [Range(0f, 1f)]
        [Tooltip("Chance that mining this tile drops its associated item.")]
        public float dropChance = 1f;

        [SerializeField]
        private EntityTag[] tags = Array.Empty<EntityTag>();

        public bool HasTag(EntityTag tag)
        {
            if (tag == null)
                return false;

            for (int i = 0; i < (tags?.Length ?? 0); i++)
            {
                if (tags[i] == tag)
                    return true;
            }

            return false;
        }
    }
}
