using UnityEngine;

namespace Project.Scripts.Gameplay
{
    [CreateAssetMenu(fileName = "Knockback Settings", menuName = "Combat/Knockback Settings")]
    public sealed class KnockbackSettings : ScriptableObject
    {
        [Min(0f)] public float minimumLaunchSpeed = 2f;
        [Min(0f)] public float maximumLaunchSpeed = 18f;
        [Min(0f)] public float speedPerPower = 0.5f;
        [Min(0f)] public float heightPerSpeed = 0.08f;
        [Min(0f)] public float maximumHeight = 2f;
        [Min(0.01f)] public float minimumAirTime = 0.25f;
        [Min(0.01f)] public float airTimePerSpeed = 0.035f;
        [Min(0.01f)] public float maximumAirTime = 0.8f;
        [Min(0f)] public float stopSpeed = 1f;
        [Min(0f)] public float wallBreakMinimumSpeed = 7f;
        [Min(0f)] public float wallDamagePerPower = 1f;
        [Range(0f, 1f)] public float breakthroughForceRetention = 0.7f;
        [Range(0f, 1f)] public float ricochetForceRetention = 0.35f;
        [Min(0f)] public float breakthroughSelfDamage = 0.15f;
        [Min(0f)] public float blockedSelfDamage = 0.08f;
        [Min(0f)] public float knockoutMinimumImpactSpeed = 9f;
        [Min(0f)] public float playerStruggleLockDuration = 0.35f;
        [Range(0.01f, 1f)] public float playerStruggleGain = 0.25f;
        [Min(0f)] public float playerStruggleDecayPerSecond = 0.08f;
        [Min(0f)] public float npcRecoveryDuration = 3f;

        private static KnockbackSettings _current;
        public static KnockbackSettings Current
        {
            get
            {
                if (_current == null)
                {
                    _current = Resources.Load<KnockbackSettings>("KnockbackSettings");
                    if (_current == null)
                        _current = CreateInstance<KnockbackSettings>();
                }
                return _current;
            }
        }
    }
}
