using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class TimedStunController : MonoBehaviour,
        IStunnable, IMovementLock, IStunState
    {
        private float _stunnedUntil;
        public bool IsMovementLocked => Time.time < _stunnedUntil;
        public bool IsStunned => IsMovementLocked;

        public void Stun(float duration)
        {
            if (duration > 0f)
                _stunnedUntil = Mathf.Max(
                    _stunnedUntil, Time.time + duration);
        }
    }
}
