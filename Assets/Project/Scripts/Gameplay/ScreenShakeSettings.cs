using UnityEngine;

namespace Project.Scripts.Gameplay
{
    [CreateAssetMenu(fileName = "ScreenShakeSettings",
        menuName = "Data/Screen Shake Settings")]
    public sealed class ScreenShakeSettings : ScriptableObject
    {
        [Min(0f)] public float maximumAmplitude = 0.35f;
        [Min(0f)] public float minimumDamageAmplitude = 0.035f;
        [Min(0f)] public float maximumDamageAmplitude = 0.14f;
        [Min(0f)] public float minimumDamageDuration = 0.08f;
        [Min(0f)] public float maximumDamageDuration = 0.18f;
        [Min(0.01f)] public float damageFrequency = 34f;
        [Range(0.001f, 1f)] public float damageForMaximumShake = 0.25f;
    }
}
