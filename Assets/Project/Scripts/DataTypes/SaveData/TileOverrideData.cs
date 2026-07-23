using System;

namespace Project.Scripts.DataTypes.SaveData
{
    public enum PersistentTileLayer : byte
    {
        Ground = 0,
        Water = 1
    }

    public enum TileOverrideKind : byte
    {
        Place = 0,
        Clear = 1
    }

    [Serializable]
    public sealed class TileOverrideData
    {
        public byte localX;
        public byte localY;
        public PersistentTileLayer layer;
        public TileOverrideKind kind;
        public int tileId = -1;

        public TileOverrideData CreateSnapshot()
        {
            return new TileOverrideData
            {
                localX = localX,
                localY = localY,
                layer = layer,
                kind = kind,
                tileId = tileId
            };
        }
    }
}
