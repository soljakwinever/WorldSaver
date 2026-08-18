using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class KnockbackImpactController : MonoBehaviour,
        IKnockbackState
    {
        private readonly HashSet<Vector3Int> _contactedWalls = new();
        private Rigidbody2D _body;
        private float _remainingPower;
        private bool _launched;
        private CollisionDetectionMode2D _previousCollisionMode;
        private Vector2 _lastTravelVelocity;

        public bool IsLaunched => _launched;

        private void Awake() => _body = GetComponent<Rigidbody2D>();

        public void Launch(Vector2 direction, float power)
        {
            if (power <= 0f)
                return;
            _body ??= GetComponent<Rigidbody2D>();
            KnockbackSettings settings = KnockbackSettings.Current;
            float speed = Mathf.Clamp(
                power * settings.speedPerPower,
                settings.minimumLaunchSpeed,
                settings.maximumLaunchSpeed);
            _remainingPower = power;
            _contactedWalls.Clear();
            if (!_launched)
                _previousCollisionMode = _body.collisionDetectionMode;
            _launched = true;
            _body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            _body.linearVelocity = direction.normalized * speed;
            _lastTravelVelocity = _body.linearVelocity;
            float height = Mathf.Min(settings.maximumHeight,
                speed * settings.heightPerSpeed);
            float duration = Mathf.Clamp(speed * settings.airTimePerSpeed,
                settings.minimumAirTime, settings.maximumAirTime);
            FalseHeightController.TryLaunch(gameObject, height, duration);
        }

        private void FixedUpdate()
        {
            if (!_launched)
                return;
            if (_body.linearVelocity.magnitude <=
                KnockbackSettings.Current.stopSpeed)
            {
                EndLaunch();
                return;
            }
            _lastTravelVelocity = _body.linearVelocity;
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (!_launched || collision.contactCount == 0)
                return;
            Vector2 velocity = _lastTravelVelocity.magnitude >
                               _body.linearVelocity.magnitude
                ? _lastTravelVelocity
                : _body.linearVelocity;
            float speed = velocity.magnitude;
            if (speed <= 0f)
                return;

            ContactPoint2D contact = collision.GetContact(0);
            Vector2 direction = velocity.normalized;
            Vector3Int cell = Vector3Int.FloorToInt(
                contact.point + direction * 0.12f);
            if (!_contactedWalls.Add(cell))
                return;

            KnockbackSettings settings = KnockbackSettings.Current;
            int wallDamage = Mathf.Max(1, Mathf.CeilToInt(
                _remainingPower * settings.wallDamagePerPower));
            bool mayBreak = speed >= settings.wallBreakMinimumSpeed;
            if (!KnockbackWallResolver.TryDamageWall(
                    contact.point,
                    direction,
                    wallDamage,
                    mayBreak,
                    out bool destroyed))
                return;

            float retention = mayBreak && destroyed
                ? settings.breakthroughForceRetention
                : settings.ricochetForceRetention;
            float damageFraction = mayBreak && destroyed
                ? settings.breakthroughSelfDamage
                : settings.blockedSelfDamage;
            ApplySelfDamage(Mathf.CeilToInt(_remainingPower * damageFraction));

            if (speed >= settings.knockoutMinimumImpactSpeed)
            {
                KnockedOutStateController knockedOut =
                    GetComponent<KnockedOutStateController>() ??
                    gameObject.AddComponent<KnockedOutStateController>();
                knockedOut.KnockOut();
                EndLaunch();
                return;
            }

            _remainingPower *= retention;
            _body.linearVelocity = mayBreak && destroyed
                ? direction * speed * retention
                : Vector2.Reflect(direction, contact.normal).normalized *
                  speed * retention;
        }

        private void ApplySelfDamage(int damage)
        {
            if (damage <= 0)
                return;
            IDamageable target = GetComponent<IDamageable>() ??
                                 GetComponentInParent<IDamageable>() ??
                                 GetComponentInChildren<IDamageable>();
            target?.TakeDamage(new AttackContext(
                gameObject, null, damage, EntityDamageSource.Environment));
        }

        private void EndLaunch()
        {
            if (!_launched)
                return;
            _launched = false;
            _remainingPower = 0f;
            _lastTravelVelocity = Vector2.zero;
            if (_body != null)
                _body.collisionDetectionMode = _previousCollisionMode;
        }

        private void OnDisable() => EndLaunch();
    }
}
