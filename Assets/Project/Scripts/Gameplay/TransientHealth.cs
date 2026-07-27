using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public sealed class TransientHealth : MonoBehaviour, IHasHealth, IDamageable
    {
        [SerializeField, Min(1)] private int maxHealth = 100;
        [SerializeField] private int health = 100;

        public int Health => health;
        public int MaxHealth => maxHealth;

        private void Awake()
        {
            health = Mathf.Clamp(health, 0, maxHealth);
        }

        public void Initialize(int maximumHealth)
        {
            if (maximumHealth < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumHealth));

            maxHealth = health = maximumHealth;
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
            TakeDamage(context.Force);
            return previousHealth - health;
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
        }
#endif
    }
}
