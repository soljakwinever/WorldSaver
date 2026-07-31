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
        public event Action Died;
        public event Action<int, int> HealthChanged;

        public const ushort TypeId = 1;
        private const ushort CurrentVersion = 2;

        [SerializeField, Min(1)] private int maxHealth = 100;
        [SerializeField] private int health = 100;
        private int _baselineMaxHealth;

        public int Health => health;
        public int MaxHealth => maxHealth;
        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => CurrentVersion;

        private void Awake()
        {
            health = Mathf.Clamp(health, 0, maxHealth);
            _baselineMaxHealth = maxHealth;
        }

        public void TakeDamage(int damage)
        {
            if (damage < 0)
                throw new ArgumentOutOfRangeException(nameof(damage));

            ApplyDamage(damage);
        }

        public int TakeDamage(AttackContext context)
        {
            return ApplyDamage(context.Force);
        }

        public void Heal(int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));

            int previousHealth = health;
            health = Mathf.Min(maxHealth, health + amount);
            RaiseHealthChanged(previousHealth);
        }

        public void SetHealth(int value)
        {
            int previousHealth = health;
            health = Mathf.Clamp(value, 0, maxHealth);
            RaiseHealthChanged(previousHealth);
            if (previousHealth > 0 && health == 0)
                Died?.Invoke();
        }

        private int ApplyDamage(int damage)
        {
            if (damage < 0)
                throw new ArgumentOutOfRangeException(nameof(damage));

            PlayerDataController player =
                GetComponent<PlayerDataController>();
            if (player != null)
                damage = player.MitigateDamage(damage);

            int previousHealth = health;
            health = Mathf.Max(0, health - damage);
            int delivered = previousHealth - health;
            RaiseHealthChanged(previousHealth);
            if (previousHealth > 0 && health == 0)
                Died?.Invoke();
            return delivered;
        }

        public void WriteState(BinaryWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            writer.Write(maxHealth);
            writer.Write(health);
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            if (savedVersion == 0 || savedVersion > CurrentVersion)
                throw new InvalidDataException($"Unsupported health state version {savedVersion}.");

            int restoredMaxHealth = savedVersion >= 2
                ? reader.ReadInt32()
                : maxHealth;
            int restoredHealth = reader.ReadInt32();
            if (restoredMaxHealth < 1)
                throw new InvalidDataException(
                    $"Saved maximum health {restoredMaxHealth} is invalid.");
            if (restoredHealth < 0 || restoredHealth > restoredMaxHealth)
                throw new InvalidDataException(
                    $"Saved health {restoredHealth} is outside the valid range 0..{restoredMaxHealth}.");

            maxHealth = restoredMaxHealth;
            health = restoredHealth;
            HealthChanged?.Invoke(health, maxHealth);
        }

        public bool IsAtBaseline()
        {
            return health == maxHealth &&
                   (_baselineMaxHealth <= 0 ||
                    maxHealth == _baselineMaxHealth);
        }

        private void OnValidate()
        {
            maxHealth = Mathf.Max(1, maxHealth);
            health = Mathf.Clamp(health, 0, maxHealth);
        }

        public void Initialize(int maximumHealth)
        {
            if (maximumHealth < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumHealth));

            _baselineMaxHealth = maxHealth = health = maximumHealth;
            HealthChanged?.Invoke(health, maxHealth);
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
            HealthChanged?.Invoke(health, maxHealth);
        }

        private void RaiseHealthChanged(int previousHealth)
        {
            if (previousHealth != health)
                HealthChanged?.Invoke(health, maxHealth);
        }
    }
}
