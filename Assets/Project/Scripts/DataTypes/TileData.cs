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
    }
}
