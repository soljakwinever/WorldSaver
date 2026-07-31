using System;
using Project.Scripts.Interface.Decorator;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerDataController))]
    [RequireComponent(typeof(PersistentHealth))]
    public sealed class PlayerNeedsController : MonoBehaviour, IHasNeeds
    {
        [Header("Health Regeneration")]
        [SerializeField, Min(0f)]
        private float fullHungerHealthRegenerationPerSecond = 0.25f;
        [SerializeField, Range(0f, 1f)]
        private float fullHungerThreshold = 0.95f;

        private PlayerDataController _player;
        private PersistentHealth _health;
        private WorldData _worldData;
        private bool _isMoving;
        private float _pendingHealthRegeneration;

        public float FullHungerHealthRegenerationPerSecond =>
            fullHungerHealthRegenerationPerSecond;
        public float FullHungerThreshold => fullHungerThreshold;
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
            if (hungerThreshold < 0f ||
                hungerThreshold > 1f ||
                float.IsNaN(hungerThreshold))
            {
                throw new ArgumentOutOfRangeException(nameof(hungerThreshold));
            }

            fullHungerHealthRegenerationPerSecond = healthPerSecond;
            fullHungerThreshold = hungerThreshold;
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
                _player.HungerDrainRate * hungerWorldRate * deltaTime;
            _player.Energy -=
                _player.EnergyDrainRate *
                movementMultiplier *
                100f / _player.MaxEnergy *
                energyWorldRate *
                deltaTime;

            RegenerateHealth(deltaTime);
        }

        private void RegenerateHealth(float deltaTime)
        {
            if (_player.Hunger < fullHungerThreshold ||
                _health.Health <= 0 ||
                _health.Health >= _health.MaxHealth ||
                fullHungerHealthRegenerationPerSecond <= 0f)
            {
                _pendingHealthRegeneration = 0f;
                return;
            }

            _pendingHealthRegeneration +=
                fullHungerHealthRegenerationPerSecond * deltaTime;
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

        private void ResolvePlayerComponents()
        {
            _player ??= GetComponent<PlayerDataController>();
            _health ??= GetComponent<PersistentHealth>();
        }

        private void OnValidate()
        {
            fullHungerHealthRegenerationPerSecond = Mathf.Max(
                0f,
                fullHungerHealthRegenerationPerSecond);
            fullHungerThreshold = Mathf.Clamp01(fullHungerThreshold);
        }
    }
}
