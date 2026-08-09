using System;
using Project.Scripts.Interface.Decorator;
using UnityEngine;
using UnityEngine.Serialization;
using Zenject;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerDataController))]
    [RequireComponent(typeof(PersistentHealth))]
    public sealed class PlayerNeedsController : MonoBehaviour, IHasNeeds
    {
        [Header("Regeneration")]
        [FormerlySerializedAs("fullHungerHealthRegenerationPerSecond")]
        [SerializeField, Min(0f)] private float healthRegenerationPerSecond = 0.25f;
        [SerializeField, Min(0f)] private float manaRegenerationPerSecond = 0.25f;
        [SerializeField, Min(1f)] private float fedRegenerationMultiplier = 2f;

        private PlayerDataController _player;
        private PersistentHealth _health;
        private WorldData _worldData;
        private bool _isMoving;
        private float _pendingHealthRegeneration;

        public float HealthRegenerationPerSecond => healthRegenerationPerSecond;
        public float ManaRegenerationPerSecond => manaRegenerationPerSecond;
        public float FedRegenerationMultiplier => fedRegenerationMultiplier;
        public float Hunger
        {
            get
            {
                ResolvePlayerComponents();
                return _player?.Hunger ?? 0f;
            }
            set
            {
                ResolvePlayerComponents();
                if (_player != null)
                    _player.Hunger = value;
            }
        }
        public float Energy
        {
            get
            {
                ResolvePlayerComponents();
                return _player?.Energy ?? 0f;
            }
            set
            {
                ResolvePlayerComponents();
                if (_player != null)
                    _player.Energy = value;
            }
        }

        [Inject]
        public void Construct(WorldData worldData)
        {
            _worldData = worldData ??
                throw new ArgumentNullException(nameof(worldData));
        }

        private void Awake()
        {
            ResolvePlayerComponents();
        }

        private void Update()
        {
            SimulateNeeds(Time.deltaTime);
        }

        public void SetMoving(bool moving)
        {
            _isMoving = moving;
        }

        public void ConfigureHealthRegeneration(
            float healthPerSecond,
            float hungerThreshold = 0.95f)
        {
            if (healthPerSecond < 0f ||
                float.IsNaN(healthPerSecond) ||
                float.IsInfinity(healthPerSecond))
            {
                throw new ArgumentOutOfRangeException(nameof(healthPerSecond));
            }
            if (hungerThreshold < 0f || hungerThreshold > 1f ||
                float.IsNaN(hungerThreshold))
            {
                throw new ArgumentOutOfRangeException(nameof(hungerThreshold));
            }

            healthRegenerationPerSecond = healthPerSecond;
            _pendingHealthRegeneration = 0f;
        }

        public void ConfigureRegeneration(
            float healthPerSecond,
            float manaPerSecond,
            float fedMultiplier)
        {
            if (!IsValidRate(healthPerSecond))
                throw new ArgumentOutOfRangeException(nameof(healthPerSecond));
            if (!IsValidRate(manaPerSecond))
                throw new ArgumentOutOfRangeException(nameof(manaPerSecond));
            if (fedMultiplier < 1f || float.IsNaN(fedMultiplier) ||
                float.IsInfinity(fedMultiplier))
                throw new ArgumentOutOfRangeException(nameof(fedMultiplier));

            healthRegenerationPerSecond = healthPerSecond;
            manaRegenerationPerSecond = manaPerSecond;
            fedRegenerationMultiplier = fedMultiplier;
            _pendingHealthRegeneration = 0f;
        }

        public void SimulateNeeds(float deltaTime)
        {
            if (deltaTime < 0f ||
                float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }
            if (deltaTime == 0f)
                return;

            ResolvePlayerComponents();
            if (_player == null || _health == null ||
                _player.IsDeathInProgress)
                return;

            WorldData.PlayerSettings settings = _worldData?.playerSettings;
            float hungerWorldRate = settings != null
                ? Mathf.Max(0f, settings.hungerRate)
                : 1f;
            float energyWorldRate = settings != null
                ? Mathf.Max(0f, settings.energyRate)
                : 1f;
            float movementMultiplier = _isMoving && settings != null
                ? Mathf.Max(0f, settings.movementEnergyMulpiplier)
                : 1f;

            _player.Hunger -=
                _player.HungerDrainRate *
                100f / _player.MaxHunger *
                hungerWorldRate * deltaTime;
            _player.Energy -=
                _player.EnergyDrainRate *
                movementMultiplier *
                100f / _player.MaxEnergy *
                energyWorldRate *
                deltaTime;

            RegenerateHealth(deltaTime);
            RegenerateMana(deltaTime);
        }

        private void RegenerateHealth(float deltaTime)
        {
            if (_health.Health <= 0 ||
                _health.Health >= _health.MaxHealth ||
                healthRegenerationPerSecond <= 0f)
            {
                _pendingHealthRegeneration = 0f;
                return;
            }

            _pendingHealthRegeneration +=
                healthRegenerationPerSecond * GetHungerMultiplier() * deltaTime;
            int wholeHealth = Mathf.FloorToInt(
                _pendingHealthRegeneration);
            if (wholeHealth <= 0)
                return;

            int missingHealth = _health.MaxHealth - _health.Health;
            int restoredHealth = Mathf.Min(wholeHealth, missingHealth);
            _health.Heal(restoredHealth);
            _pendingHealthRegeneration -= restoredHealth;
            if (_health.Health >= _health.MaxHealth)
                _pendingHealthRegeneration = 0f;
        }

        private void RegenerateMana(float deltaTime)
        {
            if (_player.Mana >= 1f ||
                manaRegenerationPerSecond <= 0f)
                return;

            _player.Mana += manaRegenerationPerSecond *
                            GetHungerMultiplier() * deltaTime /
                            _player.MaxMana;
        }

        private float GetHungerMultiplier() =>
            _player.Hunger > 0f ? fedRegenerationMultiplier : 1f;

        private static bool IsValidRate(float rate) =>
            rate >= 0f && !float.IsNaN(rate) && !float.IsInfinity(rate);

        private void ResolvePlayerComponents()
        {
            _player ??= GetComponent<PlayerDataController>();
            _health ??= GetComponent<PersistentHealth>();
        }

        private void OnValidate()
        {
            healthRegenerationPerSecond = Mathf.Max(0f, healthRegenerationPerSecond);
            manaRegenerationPerSecond = Mathf.Max(0f, manaRegenerationPerSecond);
            fedRegenerationMultiplier = Mathf.Max(1f, fedRegenerationMultiplier);
        }
    }
}
