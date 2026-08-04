using UnityEngine;

namespace Project.Scripts.DataTypes
{
    /// <summary>
    /// Packs water color, depth, and a texture-array slice into the Color32
    /// stream exposed by Unity's tilemap mesh.
    /// </summary>
    public static class WaterTilePayload
    {
        public const int MaxTextureIndex = 63;

        private const int ColorMax = 63;
        private const int IndexBitsPerChannel = 2;
        private const int IndexChannelMask = 3;

        public static Color Encode(
            Color surfaceColor,
            float normalizedDepth,
            int textureIndex)
        {
            int index = Mathf.Clamp(textureIndex, 0, MaxTextureIndex);
            return new Color32(
                PackColor(surfaceColor.r, index),
                PackColor(surfaceColor.g, index >> 2),
                PackColor(surfaceColor.b, index >> 4),
                (byte)Mathf.RoundToInt(Mathf.Clamp01(normalizedDepth) * 255f));
        }

        public static Color DecodeSurfaceColor(Color payload)
        {
            Color32 packed = payload;
            return new Color(
                UnpackColor(packed.r),
                UnpackColor(packed.g),
                UnpackColor(packed.b),
                1f);
        }

        public static float DecodeDepth(Color payload)
        {
            Color32 packed = payload;
            return packed.a / 255f;
        }

        public static int DecodeTextureIndex(Color payload)
        {
            Color32 packed = payload;
            return (packed.r & IndexChannelMask) |
                   (packed.g & IndexChannelMask) << 2 |
                   (packed.b & IndexChannelMask) << 4;
        }

        private static byte PackColor(float color, int indexBits)
        {
            int colorValue = Mathf.RoundToInt(Mathf.Clamp01(color) * ColorMax);
            return (byte)((colorValue << IndexBitsPerChannel) |
                          (indexBits & IndexChannelMask));
        }

        private static float UnpackColor(byte value) =>
            (value >> IndexBitsPerChannel) / (float)ColorMax;
    }
}
