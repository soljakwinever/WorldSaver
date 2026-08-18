using NUnit.Framework;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Tests.Editor
{
    public sealed class FalseHeightTests
    {
        [Test]
        public void Arc_StartsAndEndsGrounded_AndPeaksHalfway()
        {
            Assert.That(FalseHeightController.EvaluateArc(2f, 0f), Is.Zero);
            Assert.That(FalseHeightController.EvaluateArc(2f, 0.5f), Is.EqualTo(2f));
            Assert.That(FalseHeightController.EvaluateArc(2f, 1f), Is.Zero);
        }

        [Test]
        public void TryLaunch_UsesOptedInEntityController()
        {
            var entity = new GameObject("height-test");
            try
            {
                FalseHeightController controller =
                    entity.AddComponent<FalseHeightController>();

                Assert.That(FalseHeightController.TryLaunch(entity, 1f, 0.5f), Is.True);
                Assert.That(controller.IsAirborne, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(entity);
            }
        }

        [Test]
        public void Settings_ResolveImportedSpriteByShadowSizeName()
        {
            var texture = new Texture2D(1, 1);
            Sprite small = Sprite.Create(
                texture, new Rect(0, 0, 1, 1), Vector2.zero);
            small.name = "Shadows_0";
            var settings = ScriptableObject.CreateInstance<ShadowSpriteSettings>();
            try
            {
                var field = typeof(ShadowSpriteSettings).GetField(
                    "shadows",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null);
                field.SetValue(settings, new[] { small });

                Assert.That(settings.GetShadow(ShadowSize.Small), Is.SameAs(small));
                Assert.That(settings.GetShadow(ShadowSize.Medium), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(small);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(settings);
            }
        }
    }
}
