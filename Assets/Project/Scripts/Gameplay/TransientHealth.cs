using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public sealed class TransientHealth : MonoBehaviour, IHasHealth, IDamageable,
        IHasShadow
    {
        public event Action Died;

        [SerializeField, Min(1)] private int maxHealth = 100;
        [SerializeField] private int health = 100;
        [SerializeField, Min(0)] private int defense;
        [SerializeField, Min(0f)] private float hitStunDuration = 0.35f;

        public int Health => health;
        public int MaxHealth => maxHealth;
        public bool IsDead { get; private set; }
        private bool _deferDestructionOnDeath;
        public AttackContext? LastDamageContext { get; private set; }
        private FalseHeightController _height;
        public ShadowSize ShadowSize => Height.ShadowSize;
        public float VisualHeight => Height.VisualHeight;
        public bool IsAirborne => Height.IsAirborne;
        private FalseHeightController Height => _height ??=
            GetComponent<FalseHeightController>() ??
            gameObject.AddComponent<FalseHeightController>();

        private void Awake()
        {
            _ = Height;
            health = Mathf.Clamp(health, 0, maxHealth);
        }

        public void Launch(float peakHeight, float duration) =>
            Height.Launch(peakHeight, duration);

        public void DeferDestructionOnDeath() =>
            _deferDestructionOnDeath = true;

        public void Initialize(int maximumHealth)
        {
            Initialize(maximumHealth, 0);
        }

        public void Initialize(int maximumHealth, int damageDefense)
        {
            if (maximumHealth < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumHealth));
            if (damageDefense < 0)
                throw new ArgumentOutOfRangeException(nameof(damageDefense));

            maxHealth = health = maximumHealth;
            defense = damageDefense;
            IsDead = false;
        }
        
        public void TakeDamage(int damage)
        {
            if (damage < 0)
                throw new ArgumentOutOfRangeException(nameof(damage));

            if (!IsDead)
                health = Mathf.Max(0, health - damage);
        }

        public int TakeDamage(AttackContext context)
        {
            if (IsDead)
                return 0;

            int previousHealth = health;
            TakeDamage(Mathf.Max(0, context.Force - defense));
            int damageDelivered = previousHealth - health;
            if (damageDelivered == 0)
                return 0;

            LastDamageContext = context;

            if (health == 0)
            {
                IsDead = true;
                Died?.Invoke();
                if (!_deferDestructionOnDeath)
                    Destroy(gameObject);
            }
            else
                ApplyHitStun();

            return damageDelivered;
        }

        private void ApplyHitStun()
        {
            if (hitStunDuration <= 0f)
                return;

            MonoBehaviour[] behaviours =
                GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IStunnable stunnable)
                    stunnable.Stun(hitStunDuration);
            }
        }

        public void Heal(int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));

            health = Mathf.Min(maxHealth, health + amount);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            maxHealth = Mathf.Max(1, maxHealth);
            health = Mathf.Clamp(health, 0, maxHealth);
            defense = Mathf.Max(0, defense);
            hitStunDuration = Mathf.Max(0f, hitStunDuration);
        }
#endif
    }
}
