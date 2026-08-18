using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [Serializable]
    public struct ScreenShakeRequest
    {
        [Min(0f)] public float amplitude;
        [Min(0f)] public float duration;
        [Min(0.01f)] public float frequency;
        [Range(0f, 1f)] public float falloff;

        public ScreenShakeRequest(
            float amplitude,
            float duration,
            float frequency,
            float falloff = 1f)
        {
            this.amplitude = Mathf.Max(0f, amplitude);
            this.duration = Mathf.Max(0f, duration);
            this.frequency = Mathf.Max(0.01f, frequency);
            this.falloff = Mathf.Clamp01(falloff);
        }
    }
}
