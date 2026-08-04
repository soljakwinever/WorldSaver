using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    public enum WeaponDisplayMode : byte
    {
        FourDirectional,
        Rotate360
    }

    [Serializable]
    public struct WeaponHurtBox2D
    {
        public Vector2 center;
        public Vector2 size;
    }

    [Serializable]
    public sealed class WeaponSwingFrame
    {
        [Min(0.01f)] public float duration = 0.08f;
        public Vector2 position;
        public Vector2 scale = Vector2.one;
        [Tooltip("Rotation relative to the attack direction, in degrees.")]
        public float rotation;
        [Tooltip("The local hurt box used during this frame.")]
        public WeaponHurtBox2D hurtBox;
        public bool hurtBoxActive = true;
        [Tooltip("Allows the wielder to move for the duration of this frame.")]
        public bool canMoveWhileSwinging;
        [Tooltip("For ranged attacks, launch the projectile on this frame.")]
        public bool releaseAttack;
    }

    [CreateAssetMenu(
        fileName = "New Weapon Swing Animation",
        menuName = "Data/Combat/Weapon Swing Animation")]
    public sealed class WeaponSwingAnimation : ScriptableObject
    {
        public WeaponDisplayMode displayMode;
        public Sprite weaponSprite;
        [Tooltip("Added to the aim angle. Use this when the source sprite does not face right.")]
        public float spriteAngleOffset;
        public int sortingOrder = 1;
        public LayerMask targetLayers = ~0;
        public WeaponSwingFrame[] frames = Array.Empty<WeaponSwingFrame>();
    }
}
