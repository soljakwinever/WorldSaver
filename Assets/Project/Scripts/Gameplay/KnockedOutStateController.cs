using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class KnockedOutStateController : MonoBehaviour,
        IKnockedOutState, IMovementLock
    {
        private float _endsAt;
        private float _struggleLockedUntil;
        private float _struggle;
        private bool _isPlayer;

        public bool IsKnockedOut { get; private set; }
        public bool IsMovementLocked => IsKnockedOut;

        private void Awake() =>
            _isPlayer = GetComponentInParent<PlayerDataController>() != null;

        private void Update()
        {
            if (!IsKnockedOut)
                return;
            KnockbackSettings settings = KnockbackSettings.Current;
            if (!_isPlayer && Time.time >= _endsAt)
            {
                Recover();
                return;
            }
            if (_isPlayer && settings.playerStruggleDecayPerSecond > 0f)
                _struggle = Mathf.Max(0f, _struggle -
                    settings.playerStruggleDecayPerSecond * Time.deltaTime);
        }

        public void KnockOut()
        {
            KnockbackSettings settings = KnockbackSettings.Current;
            IsKnockedOut = true;
            _struggle = 0f;
            _struggleLockedUntil = Time.time + settings.playerStruggleLockDuration;
            _endsAt = Time.time + settings.npcRecoveryDuration;
            if (TryGetComponent(out Rigidbody2D body))
                body.linearVelocity = Vector2.zero;
            GetComponent<FalseHeightController>()?.CancelHeight();
        }

        public bool TryStruggle()
        {
            if (!IsKnockedOut || !_isPlayer || Time.time < _struggleLockedUntil)
                return false;
            _struggle = Mathf.Clamp01(
                _struggle + KnockbackSettings.Current.playerStruggleGain);
            if (_struggle >= 1f)
                Recover();
            return true;
        }

        public void Recover()
        {
            IsKnockedOut = false;
            _struggle = 0f;
            _endsAt = 0f;
        }

        private void OnDisable() => Recover();
    }
}
