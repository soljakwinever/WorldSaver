#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class WaterTilePayloadTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(17)]
        [TestCase(WaterTilePayload.MaxTextureIndex)]
        public void RoundTripPreservesTextureIndexAndDepth(int textureIndex)
        {
            Color surface = new(0.2f, 0.5f, 0.9f, 1f);
            const float depth = 0.65f;

            Color payload = WaterTilePayload.Encode(
                surface,
                depth,
                textureIndex);

            Color decoded = WaterTilePayload.DecodeSurfaceColor(payload);
            Assert.That(decoded.r, Is.EqualTo(surface.r).Within(1f / 63f));
            Assert.That(decoded.g, Is.EqualTo(surface.g).Within(1f / 63f));
            Assert.That(decoded.b, Is.EqualTo(surface.b).Within(1f / 63f));
            Assert.That(
                WaterTilePayload.DecodeDepth(payload),
                Is.EqualTo(depth).Within(1f / 255f));
            Assert.That(
                WaterTilePayload.DecodeTextureIndex(payload),
                Is.EqualTo(textureIndex));
        }

        [Test]
        public void EncodeClampsTextureIndexToSupportedRange()
        {
            Color payload = WaterTilePayload.Encode(
                Color.white,
                1f,
                WaterTilePayload.MaxTextureIndex + 1);

            Assert.That(
                WaterTilePayload.DecodeTextureIndex(payload),
                Is.EqualTo(WaterTilePayload.MaxTextureIndex));
        }
    }
}
#endif
