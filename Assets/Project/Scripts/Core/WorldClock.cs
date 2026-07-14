using UnityEngine;

namespace Project.Scripts.Core
{
    // Replace this with an adapter around TimeController once the calendar owns
    // the authoritative persistent tick.
    public sealed class WorldClock : MonoBehaviour, IWorldClock
    {
        [Min(0.01f)]
        [SerializeField] private float secondsPerTick = 1f;
        [SerializeField] private long currentTick;

        private float _accumulator;

        public long CurrentTick => currentTick;

        private void Update()
        {
            _accumulator += Time.deltaTime;

            while (_accumulator >= secondsPerTick)
            {
                _accumulator -= secondsPerTick;
                currentTick++;
            }
        }

        public void Restore(long tick)
        {
            currentTick = System.Math.Max(0, tick);
            _accumulator = 0f;
        }
    }
}
