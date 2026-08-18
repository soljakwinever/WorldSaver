using System;
using System.Collections.Generic;
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
        private GameObject _travelEffect;
        private readonly List<(Collider2D projectile, Collider2D attacker)>
            _ignoredAttackerCollisions = new();

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

            // Pooled projectiles are inactive when they arrive here. Configure
            // physics only after enabling them; enabling a Rigidbody2D can
            // rebuild its physics body and discard velocity assigned earlier.
            gameObject.SetActive(true);
            transform.SetPositionAndRotation(
                context.Origin,
                context.Projectile.rotateToDirection
                    ? Quaternion.FromToRotation(
                        Vector3.right,
                        context.Direction)
                    : transform.rotation);

            _body ??= GetComponent<Rigidbody2D>();
            _body.simulated = true;
            _body.bodyType = RigidbodyType2D.Kinematic;
            _body.gravityScale = 0f;
            // Kinematic bodies do not normally report contacts with static or
            // other kinematic bodies. World walls are static tilemap bodies,
            // so projectiles need full kinematic contacts to receive impacts.
            _body.useFullKinematicContacts = true;
            _body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            _body.WakeUp();
            _body.linearVelocity =
                context.Direction * Mathf.Max(0f, context.Projectile.speed);
            _body.angularVelocity = 0f;
            IgnoreAttackerCollisions(context.Attack.Attacker);
            if (context.TravelEffectPrefab != null)
            {
                _travelEffect = Instantiate(
                    context.TravelEffectPrefab, transform);
                _travelEffect.transform.SetLocalPositionAndRotation(
                    Vector3.zero, Quaternion.identity);
            }
        }

        internal void Deactivate()
        {
            RestoreAttackerCollisions();
            if (_body != null)
            {
                _body.linearVelocity = Vector2.zero;
                _body.angularVelocity = 0f;
            }

            _isActive = false;
            if (_travelEffect != null)
                Destroy(_travelEffect);
            _travelEffect = null;
            _attackService = null;
            _release = null;
            gameObject.SetActive(false);
        }

        private void IgnoreAttackerCollisions(GameObject attacker)
        {
            RestoreAttackerCollisions();
            if (attacker == null)
                return;

            Collider2D[] projectileColliders =
                GetComponentsInChildren<Collider2D>(true);
            Collider2D[] attackerColliders =
                attacker.GetComponentsInChildren<Collider2D>(true);
            foreach (Collider2D projectileCollider in projectileColliders)
            foreach (Collider2D attackerCollider in attackerColliders)
            {
                if (projectileCollider == null || attackerCollider == null ||
                    projectileCollider == attackerCollider)
                {
                    continue;
                }

                Physics2D.IgnoreCollision(
                    projectileCollider,
                    attackerCollider,
                    true);
                _ignoredAttackerCollisions.Add(
                    (projectileCollider, attackerCollider));
            }
        }

        private void RestoreAttackerCollisions()
        {
            foreach (var pair in _ignoredAttackerCollisions)
            {
                if (pair.projectile != null && pair.attacker != null)
                    Physics2D.IgnoreCollision(
                        pair.projectile,
                        pair.attacker,
                        false);
            }
            _ignoredAttackerCollisions.Clear();
        }

        private void Update()
        {
            if (!_isActive)
                return;

            _remainingLifetime -= Time.deltaTime;
            if (_remainingLifetime <= 0f)
            {
                Impact(transform.position);
                return;
            }

            if (_context.Destination is not Vector3 destination)
                return;
            Vector2 remaining = destination - transform.position;
            float step = Mathf.Max(0f, _context.Projectile.speed) * Time.deltaTime;
            if (remaining.sqrMagnitude <= step * step ||
                Vector2.Dot(remaining, _context.Direction) <= 0f)
            {
                transform.position = destination;
                Impact(destination);
            }
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
                Impact(transform.position);
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
                if (_context.DealDirectDamageOnImpact)
                    _attackService.Attack(damageable, _context.Attack);
                Impact(transform.position);
                return;
            }

            if (_context.Projectile.despawnOnEnvironmentHit)
                Impact(transform.position);
        }

        private void Impact(Vector3 position)
        {
            if (!_isActive)
                return;
            _context.OnImpact?.Invoke(position);
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
