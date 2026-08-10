using Project.Scripts.Interface;
using UnityEngine;
using UnityEngine.Events;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class PlaneTransitSpace : MonoBehaviour
    {
        [SerializeField] private Transform playerEntry;
        [SerializeField, Min(0f)] private float minimumTransitSeconds = 1.5f;
        [SerializeField, Min(0f)] private float arrivalAnimationSeconds = 0.5f;
        [SerializeField] private UnityEvent transitStarted;
        [SerializeField] private UnityEvent destinationReady;

        private float _startedAt;

        public bool MinimumDurationElapsed =>
            Time.unscaledTime - _startedAt >= minimumTransitSeconds;
        public float ArrivalAnimationSeconds => arrivalAnimationSeconds;

        public void Begin(PlayerDataController player, IChunkLoader chunkloader)
        {
            if (player == null)
                throw new System.ArgumentNullException(nameof(player));

            _startedAt = Time.unscaledTime;
            Vector3 entry = playerEntry != null
                ? playerEntry.position
                : transform.position;
            chunkloader?.SetTransitSpacePinned(entry, true);
            player.transform.position = entry;
            transitStarted?.Invoke();
        }

        public void NotifyDestinationReady() => destinationReady?.Invoke();
    }
}
