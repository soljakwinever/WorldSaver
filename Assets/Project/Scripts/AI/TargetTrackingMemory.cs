using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.AI
{
    /// <summary>
    /// A bounded trail of positions at which an AI could actually see its
    /// target. Hidden target movement never enters this memory.
    /// </summary>
    public sealed class TargetTrackingMemory
    {
        private readonly List<Vector3> _breadcrumbs = new();
        private float _lastSeenTime = float.NegativeInfinity;

        public int Count => _breadcrumbs.Count;
        public float LastSeenTime => _lastSeenTime;

        public void Record(
            Vector3 targetPosition,
            Vector3 observerPosition,
            float time,
            float minimumSpacing,
            int capacity)
        {
            RemoveReached(observerPosition, minimumSpacing);
            float spacing = Mathf.Max(0.01f, minimumSpacing);
            if (_breadcrumbs.Count == 0 ||
                (_breadcrumbs[^1] - targetPosition).sqrMagnitude >=
                spacing * spacing)
            {
                _breadcrumbs.Add(targetPosition);
                while (_breadcrumbs.Count > Mathf.Max(1, capacity))
                    _breadcrumbs.RemoveAt(0);
            }

            _lastSeenTime = time;
        }

        public bool TryGetNext(
            Vector3 observerPosition,
            float time,
            float lifetime,
            float arrivalTolerance,
            out Vector3 position)
        {
            if (time - _lastSeenTime > Mathf.Max(0f, lifetime))
            {
                Clear();
                position = default;
                return false;
            }

            RemoveReached(observerPosition, arrivalTolerance);
            if (_breadcrumbs.Count == 0)
            {
                position = default;
                return false;
            }

            position = _breadcrumbs[0];
            return true;
        }

        public void Clear()
        {
            _breadcrumbs.Clear();
            _lastSeenTime = float.NegativeInfinity;
        }

        private void RemoveReached(Vector3 observerPosition, float tolerance)
        {
            float distance = Mathf.Max(0.01f, tolerance);
            float distanceSquared = distance * distance;
            while (_breadcrumbs.Count > 0 &&
                   (_breadcrumbs[0] - observerPosition).sqrMagnitude <=
                   distanceSquared)
                _breadcrumbs.RemoveAt(0);
        }
    }
}
