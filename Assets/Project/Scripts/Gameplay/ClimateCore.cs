using Project.Scripts.DataTypes;
using Project.Scripts.Core;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    public sealed class ClimateCore : MonoBehaviour, IEntityComponent,
        IEntityRemovalHandler, IPersistenceInteractionGate
    {
        private const int GizmoCircleSegments = 96;
        private static readonly Color CurrentRadiusGizmoColor =
            new(0.2f, 0.85f, 1f, 0.9f);

        [SerializeField] private float temperatureOffset;
        [SerializeField] private string permanentWeatherId;
        [SerializeField] private ItemData droppedItem;
        [SerializeField, Min(1)] private int dropCount = 1;
        [SerializeField, Range(0f, 1f)] private float dropChance = 1f;

        [InjectOptional] private IClimateCoreInfluenceRegistry _registry;
        [InjectOptional] private IItemStackPickupPool _pickupPool;
        [InjectOptional] private IWorldClock _clock;

        private AreaOfEffect _areaOfEffect;
        private PersistentHealth _health;
        private bool _ready;
        private bool _removed;

        public IPersistentEntity PersistentEntity { get; set; }
        public float TemperatureOffset => temperatureOffset;
        public string PermanentWeatherId => permanentWeatherId;

        public void Initialize(
            float configuredTemperatureOffset,
            string configuredPermanentWeatherId,
            ItemData configuredDrop,
            int configuredDropCount,
            float configuredDropChance)
        {
            temperatureOffset = configuredTemperatureOffset;
            permanentWeatherId =
                configuredPermanentWeatherId?.Trim() ?? string.Empty;
            droppedItem = configuredDrop;
            dropCount = Mathf.Max(1, configuredDropCount);
            dropChance = Mathf.Clamp01(configuredDropChance);
        }

        public void SetPersistenceReady(bool ready)
        {
            if (_ready == ready)
                return;

            _ready = ready;
            if (ready)
                Activate();
            else
                Deactivate();
        }

        private void Activate()
        {
            _removed = false;
            _areaOfEffect = GetComponent<AreaOfEffect>();
            _health = GetComponent<PersistentHealth>();
            if (_health != null)
                _health.Died += OnDied;
            if (_areaOfEffect != null)
                _areaOfEffect.Changed += Publish;
            Publish();
        }

        private void Deactivate()
        {
            if (_health != null)
                _health.Died -= OnDied;
            if (_areaOfEffect != null)
                _areaOfEffect.Changed -= Publish;
        }

        private void Publish()
        {
            if (!_ready || _removed || PersistentEntity == null)
                return;

            Transform entityTransform = transform.parent;
            Vector2 position = entityTransform != null
                ? (Vector2)entityTransform.position
                : transform.position;
            float inner = _areaOfEffect?.InnerRadius ?? 0f;
            float outer = _areaOfEffect?.OuterRadius ?? 0f;
            float rate = _areaOfEffect?.SpreadRate ?? 0f;
            float amount = _areaOfEffect?.SpreadAmount ?? 0f;
            long tick = _clock?.CurrentTick ?? 0;

            bool accepted = _registry?.RegisterOrUpdate(new ClimateCoreInfluence(
                PersistentEntity.Id,
                position,
                inner,
                outer,
                rate,
                amount,
                tick,
                temperatureOffset,
                permanentWeatherId)) ?? true;
            if (accepted)
                return;

            Debug.LogError(
                "A region may contain only one ClimateCore. Removing the duplicate.",
                this);
            _removed = true;
            PersistentEntity.RemoveFromWorld();
        }

        private void OnDied()
        {
            if (_removed)
                return;

            _removed = true;
            DropItems();
            _registry?.Remove(PersistentEntity.Id);
            PersistentEntity.RemoveFromWorld();
        }

        private void DropItems()
        {
            if (droppedItem == null ||
                _pickupPool == null ||
                dropChance <= 0f ||
                (dropChance < 1f && Random.value >= dropChance))
            {
                return;
            }

            Transform node = transform.parent;
            _pickupPool.Spawn(
                droppedItem,
                dropCount,
                ItemData.Rarity.Common,
                node != null ? node.position : transform.position);
        }

        private void OnDrawGizmos()
        {
            AreaOfEffect area = _areaOfEffect;
            if (area == null)
                area = GetComponent<AreaOfEffect>();

            float radius = area != null
                ? area.EffectiveOuterRadius
                : 0f;
            if (radius <= 0f)
                return;

            Transform entityTransform = transform.parent;
            Vector3 center = entityTransform != null
                ? entityTransform.position
                : transform.position;
            Color previousColor = Gizmos.color;
            Gizmos.color = CurrentRadiusGizmoColor;

            Vector3 previous = center + Vector3.right * radius;
            for (int segment = 1;
                 segment <= GizmoCircleSegments;
                 segment++)
            {
                float angle =
                    segment * Mathf.PI * 2f / GizmoCircleSegments;
                Vector3 current = center + new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    0f);
                Gizmos.DrawLine(previous, current);
                previous = current;
            }

            Gizmos.color = previousColor;
        }

        public void OnRemovedFromWorld()
        {
            _removed = true;
            _registry?.Remove(PersistentEntity.Id);
            Deactivate();
        }
    }
}
