using Project.Scripts.Interface;
using UnityEngine;
using UnityEngine.Events;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class PlaneTransitSpace : MonoBehaviour
    {
        [SerializeField] private Transform playerEntry;
        [SerializeField] private Transform arrivalAnchor;
        [SerializeField] private GameObject confinement;
        [SerializeField, Min(0f)] private float minimumTransitSeconds = 1.5f;
        [SerializeField, Min(0f)] private float arrivalAnimationSeconds = 0.5f;
        [SerializeField] private UnityEvent transitStarted;
        [SerializeField] private UnityEvent destinationReady;

        private float _startedAt;
        private Transform _player;
        private IChunkLoader _chunkloader;
        private Vector3 _originPosition;

        public bool MinimumDurationElapsed =>
            Time.unscaledTime - _startedAt >= minimumTransitSeconds;
        public float ArrivalAnimationSeconds => arrivalAnimationSeconds;

        public void Begin(PlayerDataController player, IChunkLoader chunkloader)
        {
            if (player == null)
                throw new System.ArgumentNullException(nameof(player));

            _startedAt = Time.unscaledTime;
            _originPosition = transform.position;
            _player = player.transform;
            _chunkloader = chunkloader;
            Vector3 entry = playerEntry != null
                ? playerEntry.position
                : transform.position;
            chunkloader?.SetTransitSpacePinned(entry, true);
            _player.position = entry;
            SetConfinement(true);
            transitStarted?.Invoke();
        }

        public void NotifyDestinationReady(Vector3 landingPosition)
        {
            Vector3 anchor = arrivalAnchor != null
                ? arrivalAnchor.position
                : _player != null ? _player.position : transform.position;
            Vector3 displacement = landingPosition - anchor;
            transform.position += displacement;
            if (_player != null)
                _player.position += displacement;
            if (_player != null && _player.TryGetComponent(out Rigidbody2D body))
            {
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.position = _player.position;
            }
            destinationReady?.Invoke();
            SetConfinement(false);
        }

        public void Complete()
        {
            _chunkloader?.SetTransitSpacePinned(Vector2.zero, false);
            _chunkloader?.CompletePlaneTransition();
            ClearRuntimeState();
        }

        public void Abort()
        {
            transform.position = _originPosition;
            SetConfinement(false);
            _chunkloader?.SetTransitSpacePinned(Vector2.zero, false);
            ClearRuntimeState();
        }

        private void ClearRuntimeState()
        {
            _player = null;
            _chunkloader = null;
        }

        private void SetConfinement(bool active)
        {
            if (confinement == null)
            {
                Transform candidate = transform.Find("Collision") ??
                                      transform.Find("Colision");
                if (candidate != null)
                    confinement = candidate.gameObject;
            }
            confinement?.SetActive(active);
        }
    }
}
