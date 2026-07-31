using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public readonly struct TownEffectExecution
    {
        public TownEffectExecution(long activeTicks, float manaSpent)
        {
            ActiveTicks = activeTicks;
            ManaSpent = manaSpent;
        }

        public long ActiveTicks { get; }
        public float ManaSpent { get; }
        public bool PerformedWork => ActiveTicks > 0;
    }

    public abstract class TownEffect : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Stable save identifier. Do not change after shipping.")]
        private string persistentId;

        [SerializeField, Min(0f)]
        private float manaCostPerTick = 1f;

        public string PersistentId => persistentId;
        public float ManaCostPerTick => Mathf.Max(0f, manaCostPerTick);

        /// <summary>
        /// Attempts to apply this effect for the elapsed world ticks. The
        /// returned execution describes actual work, so TownCore only charges
        /// mana and suppresses replenishment while the effect was operating.
        /// </summary>
        public abstract TownEffectExecution Apply(
            TownCore town,
            long elapsedTicks,
            float availableMana);

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            manaCostPerTick = Mathf.Max(0f, manaCostPerTick);
        }
#endif
    }
}
