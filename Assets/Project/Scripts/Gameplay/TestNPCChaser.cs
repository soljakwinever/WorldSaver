using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    [RequireComponent(typeof(CircleCollider2D))]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class TestNPCChaser : MonoBehaviour, IStunnable
    {
        [SerializeField, Min(0.1f)] private float movementSpeed = 3f;
        [SerializeField, Min(0.1f)] private float detectionRadius = 7f;
        [SerializeField, Min(0.05f)] private float repathInterval = 0.35f;
        [SerializeField, Min(0.01f)] private float waypointTolerance = 0.08f;
        [SerializeField, Range(1, 8)] private int headingLookAheadPoints = 3;
        [SerializeField, Min(0.01f)] private float headingSmoothTime = 0.12f;
        [SerializeField, Min(1)] private int maximumVisitedTiles = 4096;

        private readonly List<Vector2Int> _path = new();
        private IPathFindingService _pathFinder;
        private Rigidbody2D _body;
        private Transform _player;
        private int _waypointIndex;
        private float _nextRepathTime;
        private Vector2Int _plannedDestination;
        private bool _hasPlannedDestination;
        private Vector2 _heading;
        private Vector2 _headingVelocity;
        private Task<List<Vector2Int>> _pendingPath;
        private CancellationTokenSource _pathCancellation;
        private Vector2Int _pendingDestination;
        private float _stunnedUntil;

        [Inject]
        public void Construct(IPathFindingService pathFinder)
        {
            _pathFinder = pathFinder;
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            if (_body == null)
                _body = gameObject.AddComponent<Rigidbody2D>();
            _body.bodyType = RigidbodyType2D.Kinematic;
            _body.gravityScale = 0f;

            CircleCollider2D detection = GetComponent<CircleCollider2D>();
            if (detection == null)
                detection = gameObject.AddComponent<CircleCollider2D>();
            detection.isTrigger = true;
            detection.radius = detectionRadius;
            //EnsureTestVisual();
        }

        private void EnsureTestVisual()
        {
            if (GetComponentInChildren<SpriteRenderer>() != null)
                return;

            GameObject visual = new("Test NPC Visual");
            visual.transform.SetParent(transform, false);
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = CreateCircleSprite();
            renderer.color = new Color(0.85f, 0.15f, 0.15f, 1f);
            renderer.sortingOrder = 10;
        }

        private static Sprite CreateCircleSprite()
        {
            const int size = 32;
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
            {
                name = "Test NPC Circle",
                filterMode = FilterMode.Point
            };
            Color[] pixels = new Color[size * size];
            Vector2 center = new((size - 1) * 0.5f, (size - 1) * 0.5f);
            float radiusSquared = (size * 0.45f) * (size * 0.45f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] =
                    ((new Vector2(x, y) - center).sqrMagnitude <= radiusSquared)
                        ? Color.white
                        : Color.clear;
            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(
                texture,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                size);
        }

        private void OnEnable()
        {
            _path.Clear();
            _player = null;
            _waypointIndex = 0;
            _nextRepathTime = 0f;
            _hasPlannedDestination = false;
            _heading = Vector2.zero;
            _headingVelocity = Vector2.zero;
            _pendingPath = null;
            _pathCancellation = new CancellationTokenSource();
        }

        private void Update()
        {
            if (Time.time < _stunnedUntil)
                return;

            ApplyCompletedPath();

            if (_player == null || _pathFinder == null ||
                Time.time < _nextRepathTime ||
                (_pendingPath != null && !_pendingPath.IsCompleted))
                return;

            _nextRepathTime = Time.time + repathInterval;
            Vector2Int start = Vector2Int.FloorToInt(_body.position);
            Vector2Int destination =
                Vector2Int.FloorToInt((Vector2)_player.position);

            // Replacing an active path with an equivalent path resets the
            // waypoint index. Near cell borders that can repeatedly select a
            // waypoint behind the NPC and make it oscillate.
            if (_hasPlannedDestination &&
                destination == _plannedDestination &&
                _waypointIndex < _path.Count)
                return;

            _pendingDestination = destination;
            _pendingPath = _pathFinder.FindPathAsync(
                start,
                destination,
                maximumVisitedTiles,
                _pathCancellation.Token);
        }

        private void ApplyCompletedPath()
        {
            if (_pendingPath == null || !_pendingPath.IsCompleted)
                return;

            Task<List<Vector2Int>> completed = _pendingPath;
            _pendingPath = null;

            if (completed.IsCanceled)
                return;
            if (completed.IsFaulted)
            {
                Debug.LogException(completed.Exception, this);
                return;
            }

            List<Vector2Int> result = completed.Result;
            _path.Clear();
            if (result == null)
            {
                _waypointIndex = 0;
                _hasPlannedDestination = false;
                return;
            }

            _path.AddRange(result);
            _plannedDestination = _pendingDestination;
            _hasPlannedDestination = true;
            _waypointIndex = _path.Count > 1 ? 1 : 0;
            SkipReachedWaypoints();
        }

        private void FixedUpdate()
        {
            if (Time.time < _stunnedUntil)
                return;

            if (_player == null || _waypointIndex >= _path.Count)
                return;

            SkipReachedWaypoints();
            if (_waypointIndex >= _path.Count)
                return;

            // A Vector2Int path point identifies a tile, not its lower-left
            // world-space corner. Following the corner places the body exactly
            // on FloorToInt boundaries and lets tiny floating-point changes
            // make the perceived current tile flip back and forth.
            Vector2 waypoint = CellCenter(_path[_waypointIndex]);
            Vector2 desiredHeading = SampleHeadingAhead();
            _heading = Vector2.SmoothDamp(
                _heading,
                desiredHeading,
                ref _headingVelocity,
                headingSmoothTime,
                Mathf.Infinity,
                Time.fixedDeltaTime);
            if (_heading.sqrMagnitude > 0.0001f)
                _heading.Normalize();

            Vector2 next =
                _body.position +
                _heading * (movementSpeed * Time.fixedDeltaTime);
            _body.MovePosition(next);

            Vector2 toWaypoint = waypoint - next;
            Vector2 incomingSegment = _waypointIndex > 0
                ? waypoint - CellCenter(_path[_waypointIndex - 1])
                : waypoint - _body.position;
            bool passedWaypoint =
                incomingSegment.sqrMagnitude > 0.0001f &&
                Vector2.Dot(toWaypoint, incomingSegment) <= 0f;
            if (toWaypoint.sqrMagnitude <=
                    waypointTolerance * waypointTolerance ||
                passedWaypoint)
                _waypointIndex++;
        }

        private Vector2 SampleHeadingAhead()
        {
            Vector2 blendedHeading = Vector2.zero;
            float totalWeight = 0f;
            int sampleEnd = Mathf.Min(
                _path.Count,
                _waypointIndex + headingLookAheadPoints);

            for (int i = _waypointIndex; i < sampleEnd; i++)
            {
                Vector2 toSample = CellCenter(_path[i]) - _body.position;
                if (toSample.sqrMagnitude <= 0.0001f)
                    continue;

                // The active waypoint has the strongest influence so the NPC
                // remains faithful to the valid path. Later samples gradually
                // bend its ideal heading toward upcoming turns.
                float weight = 1f / (i - _waypointIndex + 1f);
                blendedHeading += toSample.normalized * weight;
                totalWeight += weight;
            }

            return totalWeight > 0f
                ? (blendedHeading / totalWeight).normalized
                : Vector2.zero;
        }

        private void SkipReachedWaypoints()
        {
            float toleranceSquared = waypointTolerance * waypointTolerance;
            while (_waypointIndex < _path.Count &&
                   (_body.position - CellCenter(_path[_waypointIndex])).sqrMagnitude <=
                   toleranceSquared)
                _waypointIndex++;
        }

        private static Vector2 CellCenter(Vector2Int cell) =>
            new(cell.x + 0.5f, cell.y + 0.5f);

        private void OnTriggerEnter2D(Collider2D other)
        {
            PlayerDataController player =
                other.GetComponentInParent<PlayerDataController>();
            if (player == null)
                return;

            _player = player.transform;
            _nextRepathTime = 0f;
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            PlayerDataController player =
                other.GetComponentInParent<PlayerDataController>();
            if (player == null || player.transform != _player)
                return;

            _player = null;
            CancelPendingPath();
            _path.Clear();
            _waypointIndex = 0;
            _hasPlannedDestination = false;
            _heading = Vector2.zero;
            _headingVelocity = Vector2.zero;
            _body.linearVelocity = Vector2.zero;
        }

        private void OnDisable()
        {
            CancelPendingPath(false);
        }

        public void Stun(float duration)
        {
            if (duration <= 0f)
                return;

            _stunnedUntil = Mathf.Max(
                _stunnedUntil,
                Time.time + duration);
            _heading = Vector2.zero;
            _headingVelocity = Vector2.zero;
            _body.linearVelocity = Vector2.zero;
        }

        private void CancelPendingPath(bool createReplacement = true)
        {
            _pathCancellation?.Cancel();
            _pathCancellation?.Dispose();
            _pathCancellation = createReplacement
                ? new CancellationTokenSource()
                : null;
            _pendingPath = null;
        }
    }
}
