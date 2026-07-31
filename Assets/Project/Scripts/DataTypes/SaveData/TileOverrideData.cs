using System;

namespace Project.Scripts.DataTypes.SaveData
{
    public enum PersistentTileLayer : byte
    {
        Ground = 0,
        Water = 1,
        Wall = 2,
        Ceiling = 3
    }

    public enum TileOverrideKind : byte
    {
        Place = 0,
        Clear = 1
    }

    public enum PersistentTileTint : byte
    {
        TileDefault = 0,
        BiomeGround = 1,
        BiomePath = 2,
        BiomeWater = 3,
        BiomeCliff = 4,
        BiomeBeach = 5,
        BiomeDirt = 6
    }

    [Serializable]
    public sealed class TileOverrideData
    {
        public byte localX;
        public byte localY;
        public PersistentTileLayer layer;
        public TileOverrideKind kind;
        public int tileId = -1;
        public PersistentTileTint tint;

        public TileOverrideData CreateSnapshot()
        {
            return new TileOverrideData
            {
                localX = localX,
                localY = localY,
                layer = layer,
                kind = kind,
                tileId = tileId,
                tint = tint
            };
        }
    }

    [Serializable]
    public sealed class WallHealthData
    {
        public byte localX;
        public byte localY;
        public byte health;

        public WallHealthData CreateSnapshot()
        {
            return new WallHealthData
            {
                localX = localX,
                localY = localY,
                health = health
            };
        }
    }

    public enum WallDestructionType : byte
    {
        TornDown = 0,
        Burned = 1,
        Destroyed = 2
    }
}
