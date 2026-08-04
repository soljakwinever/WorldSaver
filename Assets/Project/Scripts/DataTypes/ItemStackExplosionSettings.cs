using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [Serializable]
    public sealed class ItemStackExplosionSettings
    {
        [Min(0f)]
        [Tooltip("Default outward impulse used when a caller does not provide one.")]
        public float defaultImpulse = 2.5f;

        [Range(0f, 1f)]
        [Tooltip("Default random variation applied above and below the outward impulse.")]
        public float defaultImpulseVariation = 0.25f;
    }
}
