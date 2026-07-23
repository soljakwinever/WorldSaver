using UnityEngine;

namespace Project.Scripts.DataTypes.SaveData
{
    public static class WorldPartition
    {
        public const int RegionSizeInChunks = 8;

        public static Vector2Int WorldToChunk(Vector2 worldPosition)
        {
            return Vector2Int.FloorToInt(
                worldPosition / ChunkBuildResult.ChunkSize);
        }

        public static Vector2Int ChunkToRegion(Vector2Int chunkPosition)
        {
            return new Vector2Int(
                FloorDiv(chunkPosition.x, RegionSizeInChunks),
                FloorDiv(chunkPosition.y, RegionSizeInChunks));
        }

        public static Vector2Int ChunkToLocalRegionCoordinate(
            Vector2Int chunkPosition)
        {
            return new Vector2Int(
                PositiveMod(chunkPosition.x, RegionSizeInChunks),
                PositiveMod(chunkPosition.y, RegionSizeInChunks));
        }

        // Accepts a global chunk coordinate and safely handles negative chunks.
        public static ushort GetLocalChunkIndex(Vector2Int chunkPosition)
        {
            Vector2Int local = ChunkToLocalRegionCoordinate(chunkPosition);

            return checked((ushort)(
                local.y * RegionSizeInChunks + local.x));
        }

        public static Vector2Int GetLocalChunkCoordinate(ushort index)
        {
            if (index >= RegionSizeInChunks * RegionSizeInChunks)
                throw new System.ArgumentOutOfRangeException(nameof(index));

            return new Vector2Int(
                index % RegionSizeInChunks,
                index / RegionSizeInChunks);
        }

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;

            if (remainder != 0 && value < 0)
                quotient--;

            return quotient;
        }

        private static int PositiveMod(int value, int modulus)
        {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }
    }
}
