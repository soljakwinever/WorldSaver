using Project.Scripts.Core;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts
{
    /// <summary>Reserves a rectangular tile footprint for an entity.</summary>
    public sealed class SpaceReservationComponent :
        MonoBehaviour,
        IEntityComponent,
        IEntityRemovalHandler
    {
        private IPersistentEntity _persistentEntity;
        private Transform _target;
        private int _width;
        private int _height;
        private Vector2Int _registeredOrigin;
        private bool _isRegistered;

        public IPersistentEntity PersistentEntity
        {
            get => _persistentEntity;
            set
            {
                if (ReferenceEquals(_persistentEntity, value))
                    return;

                ReleaseReservation();
                _persistentEntity = value;
                RefreshReservation();
            }
        }

        public RectInt ReservedArea => new(
            GetOrigin(),
            new Vector2Int(_width, _height));

        public void Initialize(
            Transform target,
            int width,
            int height)
        {
            ReleaseReservation();
            _target = target != null ? target : transform;
            _width = Mathf.Max(1, width);
            _height = Mathf.Max(1, height);
            RefreshReservation();
        }

        public bool RefreshReservation()
        {
            if (!isActiveAndEnabled ||
                _persistentEntity == null ||
                _target == null ||
                _width <= 0 ||
                _height <= 0)
            {
                return false;
            }

            Vector2Int origin = GetOrigin();
            if (_isRegistered && origin == _registeredOrigin)
                return true;

            TileReservationSystem.Release(_persistentEntity);
            _isRegistered = TileReservationSystem.TryReserve(
                _persistentEntity,
                new RectInt(origin, new Vector2Int(_width, _height)));
            _registeredOrigin = origin;
            return _isRegistered;
        }

        public void ReleaseReservation()
        {
            if (_persistentEntity != null)
                TileReservationSystem.Release(_persistentEntity);
            _isRegistered = false;
        }

        public void OnRemovedFromWorld() => ReleaseReservation();

        private void LateUpdate()
        {
            if (_target != null && GetOrigin() != _registeredOrigin)
                RefreshReservation();
        }

        private void OnEnable() => RefreshReservation();
        private void OnDisable() => ReleaseReservation();
        private void OnDestroy() => ReleaseReservation();

        private Vector2Int GetOrigin()
        {
            if (_target == null)
                return Vector2Int.zero;

            // The entity is positioned at the exact center of its reserved
            // footprint. Subtracting its half-size recovers the tile origin,
            // including for even-sized and negatively positioned footprints.
            Vector2 origin = (Vector2)_target.position -
                             new Vector2(_width * 0.5f, _height * 0.5f);
            return Vector2Int.RoundToInt(origin);
        }
    }
}
