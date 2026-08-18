using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(
        fileName = "DangerSettings",
        menuName = "World Saver/Audio/Danger Settings")]
    public sealed class DangerSettings : ScriptableObject
    {
        [Min(0f), Tooltip("World-space radius around the player in which enemies contribute danger.")]
        public float detectionRadius = 12f;
        [Min(0.05f), Tooltip("Seconds between Danger parameter updates.")]
        public float updateInterval = 1f;
    }
}
