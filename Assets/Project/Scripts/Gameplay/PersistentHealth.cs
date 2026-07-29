using System;
using System.IO;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public sealed class PersistentHealth : MonoBehaviour, IHasHealth, IDamageable,
        IPersistentComponent
    {
        public const ushort TypeId = 1;
        private const ushort CurrentVersion = 1;

        [SerializeField, Min(1)] private int maxHealth = 100;
        [SerializeField] private int health = 100;

        public int Health => health;
        public int MaxHealth => maxHealth;
        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => CurrentVersion;

        private void Awake()
        {
            health = Mathf.Clamp(health, 0, maxHealth);
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

        public void WriteState(BinaryWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            writer.Write(health);
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            if (savedVersion != CurrentVersion)
                throw new InvalidDataException($"Unsupported health state version {savedVersion}.");

            int restoredHealth = reader.ReadInt32();
            if (restoredHealth < 0 || restoredHealth > maxHealth)
                throw new InvalidDataException(
                    $"Saved health {restoredHealth} is outside the valid range 0..{maxHealth}.");

            health = restoredHealth;
        }

        public bool IsAtBaseline()
        {
            return health == maxHealth;
        }

        private void OnValidate()
        {
            maxHealth = Mathf.Max(1, maxHealth);
            health = Mathf.Clamp(health, 0, maxHealth);
        }

        public void Initialize(int maximumHealth)
        {
            this.maxHealth = health = maximumHealth;
        }

        public void SetMaxHealth(int maximumHealth, bool healIncrease = true)
        {
            if (maximumHealth < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumHealth));

            int increase = maximumHealth - maxHealth;
            maxHealth = maximumHealth;
            health = healIncrease && increase > 0
                ? Mathf.Min(maxHealth, health + increase)
                : Mathf.Clamp(health, 0, maxHealth);
        }
    }
}
