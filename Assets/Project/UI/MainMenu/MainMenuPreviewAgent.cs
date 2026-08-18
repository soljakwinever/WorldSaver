using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.UI.MainMenu
{
    public sealed class MainMenuPreviewAgent : MonoBehaviour
    {
        private Func<Vector2Int, bool> _isWalkable;
        private BoundsInt _bounds;
        private float _speed;
        private Vector2 _target;
        private float _pause;
        private SpriteRenderer[] _sprites = Array.Empty<SpriteRenderer>();

        public void Initialize(
            Func<Vector2Int, bool> isWalkable,
            BoundsInt bounds,
            float speed)
        {
            _isWalkable = isWalkable;
            _bounds = bounds;
            _speed = Mathf.Clamp(speed, 0.5f, 2.5f);
            _sprites = GetComponentsInChildren<SpriteRenderer>(true);
            ChooseTarget();
        }

        private void Update()
        {
            if (_isWalkable == null)
                return;
            if (_pause > 0f)
            {
                _pause -= Time.unscaledDeltaTime;
                return;
            }

            Vector2 position = transform.position;
            Vector2 next = Vector2.MoveTowards(
                position,
                _target,
                _speed * Time.unscaledDeltaTime);
            Vector2 direction = next - position;
            transform.position = new Vector3(next.x, next.y, transform.position.z);
            if (Mathf.Abs(direction.x) > 0.001f)
                foreach (SpriteRenderer sprite in _sprites)
                    sprite.flipX = direction.x < 0f;

            if ((next - _target).sqrMagnitude < 0.01f)
            {
                _pause = UnityEngine.Random.Range(0.35f, 1.25f);
                ChooseTarget();
            }
        }

        private void ChooseTarget()
        {
            Vector2Int origin = Vector2Int.FloorToInt(transform.position);
            List<Vector2Int> candidates = new();
            for (int attempt = 0; attempt < 24; attempt++)
            {
                Vector2Int candidate = origin + new Vector2Int(
                    UnityEngine.Random.Range(-8, 9),
                    UnityEngine.Random.Range(-8, 9));
                if (_bounds.Contains(new Vector3Int(candidate.x, candidate.y, 0)) &&
                    _isWalkable(candidate) &&
                    HasWalkableLine(origin, candidate))
                {
                    candidates.Add(candidate);
                }
            }

            Vector2Int selected = candidates.Count > 0
                ? candidates[UnityEngine.Random.Range(0, candidates.Count)]
                : origin;
            _target = selected + new Vector2(0.5f, 0.5f);
        }

        private bool HasWalkableLine(Vector2Int from, Vector2Int to)
        {
            int steps = Mathf.Max(Mathf.Abs(to.x - from.x), Mathf.Abs(to.y - from.y));
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)Mathf.Max(1, steps);
                Vector2Int cell = new(
                    Mathf.RoundToInt(Mathf.Lerp(from.x, to.x, t)),
                    Mathf.RoundToInt(Mathf.Lerp(from.y, to.y, t)));
                if (!_isWalkable(cell))
                    return false;
            }
            return true;
        }
    }
}
