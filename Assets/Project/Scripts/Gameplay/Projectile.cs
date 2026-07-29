using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class Projectile : MonoBehaviour
    {
        private IAttackService _attackService;
        private Action<Projectile> _release;
        private ProjectileLaunchContext _context;
        private Rigidbody2D _body;
        private float _remainingLifetime;
        private bool _isActive;

        public bool IsActive => _isActive;
        public ProjectileData Data =>
            _isActive ? _context.Projectile : null;

        internal void Launch(
            ProjectileLaunchContext context,
            IAttackService attackService,
            Action<Projectile> release)
        {
            _context = context;
            _attackService = attackService ??
                throw new ArgumentNullException(nameof(attackService));
            _release = release ??
                throw new ArgumentNullException(nameof(release));
            _remainingLifetime = Mathf.Max(
                0.01f,
                context.Projectile.lifetime);
            _isActive = true;

            transform.SetPositionAndRotation(
                context.Origin,
                context.Projectile.rotateToDirection
                    ? Quaternion.FromToRotation(
                        Vector3.right,
                        context.Direction)
                    : transform.rotation);

            _body ??= GetComponent<Rigidbody2D>();
            _body.bodyType = RigidbodyType2D.Kinematic;
            _body.gravityScale = 0f;
            _body.linearVelocity =
                context.Direction * Mathf.Max(0f, context.Projectile.speed);
            _body.angularVelocity = 0f;

            gameObject.SetActive(true);
        }

        internal void Deactivate()
        {
            if (_body != null)
            {
                _body.linearVelocity = Vector2.zero;
                _body.angularVelocity = 0f;
            }

            _isActive = false;
            _attackService = null;
            _release = null;
            gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!_isActive)
                return;

            _remainingLifetime -= Time.deltaTime;
            if (_remainingLifetime <= 0f)
                Release();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            HandleHit(other);
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            HandleHit(collision.collider);
        }

        private void HandleHit(Collider2D other)
        {
            if (!_isActive || other == null ||
                !IsLayerIncluded(
                    _context.Projectile.collisionMask,
                    other.gameObject.layer))
            {
                return;
            }

            Transform attacker = _context.Attack.Attacker != null
                ? _context.Attack.Attacker.transform
                : null;
            Transform hitTransform = other.attachedRigidbody != null
                ? other.attachedRigidbody.transform
                : other.transform;
            if (attacker == null)
            {
                Release();
                return;
            }
            if (hitTransform == attacker ||
                hitTransform.IsChildOf(attacker) ||
                attacker.IsChildOf(hitTransform))
            {
                return;
            }

            IDamageable damageable =
                other.GetComponentInParent<IDamageable>() ??
                other.GetComponentInChildren<IDamageable>();
            if (damageable != null)
            {
                _attackService.Attack(damageable, _context.Attack);
                Release();
                return;
            }

            if (_context.Projectile.despawnOnEnvironmentHit)
                Release();
        }

        private void Release()
        {
            if (!_isActive)
                return;

            Action<Projectile> release = _release;
            _isActive = false;
            release?.Invoke(this);
        }

        private static bool IsLayerIncluded(
            LayerMask mask,
            int layer)
        {
            return (mask.value & (1 << layer)) != 0;
        }
    }
}
