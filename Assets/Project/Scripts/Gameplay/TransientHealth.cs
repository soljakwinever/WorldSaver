using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public sealed class TransientHealth : MonoBehaviour, IHasHealth, IDamageable
    {
        public event Action Died;

        [SerializeField, Min(1)] private int maxHealth = 100;
        [SerializeField] private int health = 100;
        [SerializeField, Min(0)] private int defense;
        [SerializeField, Min(0f)] private float knockbackImpulse = 5f;
        [SerializeField, Min(0f)] private float hitStunDuration = 0.35f;

        public int Health => health;
        public int MaxHealth => maxHealth;

        private void Awake()
        {
            health = Mathf.Clamp(health, 0, maxHealth);
        }

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
        }
        
        public void TakeDamage(int damage)
        {
            if (damage < 0)
                throw new ArgumentOutOfRangeException(nameof(damage));

            health = Mathf.Max(0, health - damage);
        }

        public int TakeDamage(AttackContext context)
        {
            int previousHealth = health;
            TakeDamage(Mathf.Max(0, context.Force - defense));
            int damageDelivered = previousHealth - health;
            if (damageDelivered == 0)
                return 0;

            ApplyKnockback(context.Attacker.transform.position);

            if (health == 0)
            {
                Died?.Invoke();
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

        private void ApplyKnockback(Vector3 attackerPosition)
        {
            if (knockbackImpulse <= 0f ||
                !TryGetComponent(out Rigidbody2D body))
                return;

            Vector2 direction =
                (transform.position - attackerPosition).normalized;
            if (direction == Vector2.zero)
                direction = Vector2.up;

            body.AddForce(direction * knockbackImpulse, ForceMode2D.Impulse);
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
            knockbackImpulse = Mathf.Max(0f, knockbackImpulse);
            hitStunDuration = Mathf.Max(0f, hitStunDuration);
        }
#endif
    }
}
