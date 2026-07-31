using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    [DisallowMultipleComponent]
    public sealed class EntityDamageVisual : MonoBehaviour,
        IPersistenceInteractionGate
    {
        [InjectOptional] private WallDamageVisualPool _pool;

        private PersistentHealth _health;
        private SpriteRenderer _entityRenderer;
        private WallDamageVisual _visual;
        private bool _ready;

        public void Initialize(PersistentHealth health)
        {
            if (_health != null)
                _health.HealthChanged -= OnHealthChanged;

            ReleaseVisual();
            _health = health;
            _entityRenderer = FindEntityRenderer();

            if (_health != null)
                _health.HealthChanged += OnHealthChanged;
            Refresh();
        }

        public void SetPersistenceReady(bool ready)
        {
            _ready = ready;
            Refresh();
        }

        private void OnHealthChanged(int health, int maximumHealth)
        {
            Refresh();
        }

        private void Refresh()
        {
            if (!_ready ||
                _pool == null ||
                _health == null ||
                _entityRenderer == null ||
                _health.Health >= _health.MaxHealth ||
                _health.Health <= 0)
            {
                ReleaseVisual();
                return;
            }

            if (_visual == null)
            {
                _visual = _pool.Spawn(
                    _health.Health,
                    _health.MaxHealth);
                _visual.ConfigureEntityMask(_entityRenderer);
            }
            else
            {
                _visual.SetHealth(
                    _health.Health,
                    _health.MaxHealth);
            }
        }

        private SpriteRenderer FindEntityRenderer()
        {
            SpriteRenderer[] renderers =
                GetComponentsInChildren<SpriteRenderer>(
                    includeInactive: true);
            foreach (SpriteRenderer candidate in renderers)
            {
                if (candidate != null &&
                    candidate.sprite != null &&
                    candidate.GetComponent<WallDamageVisual>() == null)
                {
                    return candidate;
                }
            }
            return null;
        }

        private void ReleaseVisual()
        {
            if (_visual == null)
                return;

            WallDamageVisual visual = _visual;
            _visual = null;
            _pool?.Despawn(visual);
        }

        private void OnDisable()
        {
            _ready = false;
            ReleaseVisual();
        }

        private void OnDestroy()
        {
            if (_health != null)
                _health.HealthChanged -= OnHealthChanged;
            ReleaseVisual();
        }
    }
}
