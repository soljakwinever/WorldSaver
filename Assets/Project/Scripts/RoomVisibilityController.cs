using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;
using Zenject;

namespace Project.Scripts
{
    public static class RoomVisibilityMaskBuilder
    {
        public static int Build(
            BoundsInt cellBounds,
            ISet<Vector3Int> visibleCells,
            List<Vector3> vertices,
            List<int> triangles)
        {
            if (visibleCells == null)
                throw new ArgumentNullException(nameof(visibleCells));
            if (vertices == null)
                throw new ArgumentNullException(nameof(vertices));
            if (triangles == null)
                throw new ArgumentNullException(nameof(triangles));

            vertices.Clear();
            triangles.Clear();
            int quadCount = 0;
            for (int y = cellBounds.yMin; y < cellBounds.yMax; y++)
            {
                for (int x = cellBounds.xMin; x < cellBounds.xMax; x++)
                {
                    if (visibleCells.Contains(new Vector3Int(x, y, 0)))
                        continue;

                    int vertex = vertices.Count;
                    vertices.Add(new Vector3(x, y, 0f));
                    vertices.Add(new Vector3(x + 1f, y, 0f));
                    vertices.Add(new Vector3(x + 1f, y + 1f, 0f));
                    vertices.Add(new Vector3(x, y + 1f, 0f));
                    triangles.Add(vertex);
                    triangles.Add(vertex + 2);
                    triangles.Add(vertex + 1);
                    triangles.Add(vertex);
                    triangles.Add(vertex + 3);
                    triangles.Add(vertex + 2);
                    quadCount++;
                }
            }

            return quadCount;
        }
    }

    /// <summary>
    /// Tracks the room occupied by the player, fades that room's roof, and
    /// draws an opaque tile-aligned mask over every visible cell outside it.
    /// </summary>
    public sealed class RoomVisibilityController : MonoBehaviour
    {
        private const int OutsideMaskSortingOrder = 30;
        private const int PlayerSortingOrder = 31;
        private const int CameraPaddingCells = 2;

        [Inject] private PlayerDataController _player;
        [Inject] private RoomDetectionSystem _rooms;
        [Inject] private WorldData _worldData;
        [Inject] private Grid _grid;

        private readonly HashSet<Vector3Int> _visibleCells = new();
        private readonly List<Vector3> _vertices = new();
        private readonly List<int> _triangles = new();
        private readonly List<PlayerRendererState> _playerRenderers = new();

        private Camera _camera;
        private Mesh _maskMesh;
        private MeshRenderer _maskRenderer;
        private Material _maskMaterial;
        private Room _displayRoom;
        private BoundsInt _lastCameraBounds;
        private bool _maskGeometryDirty;
        private bool _playerRaised;
        private float _outsideAlpha;

        public Room CurrentRoom { get; private set; }
        public bool IsPlayerInsideRoom => CurrentRoom != null;

        public event Action<Room> RoomChanged;

        private void Awake()
        {
            CreateMaskRenderer();
        }

        private void Start()
        {
            _camera = Camera.main;
            CachePlayerRenderers();
        }

        private void LateUpdate()
        {
            if (_player == null || _rooms == null || _grid == null)
                return;

            Room detectedRoom = DetectPlayerRoom();
            if (!ReferenceEquals(CurrentRoom, detectedRoom))
            {
                CurrentRoom = detectedRoom;
                RoomChanged?.Invoke(CurrentRoom);
                if (CurrentRoom != null)
                    SetDisplayRoom(CurrentRoom);
            }

            bool inside = CurrentRoom != null;
            _outsideAlpha = Mathf.MoveTowards(
                _outsideAlpha,
                inside ? 1f : 0f,
                Time.unscaledDeltaTime /
                Mathf.Max(0.01f, _worldData.roomOutsideFadeSeconds));

            _rooms.SetActiveRoomRoofVisibility(
                inside ? CurrentRoom : null,
                inside ? 0f : 1f);

            if (!inside &&
                Mathf.Approximately(_outsideAlpha, 0f))
            {
                SetDisplayRoom(null);
            }

            UpdateMask();
            SetPlayerRaised(_outsideAlpha > 0.001f);
        }

        private Room DetectPlayerRoom()
        {
            Vector3Int playerCell =
                _grid.WorldToCell(_player.transform.position);
            if (_rooms.TryGetRoom(playerCell, out Room room))
                return room;

            return CurrentRoom != null &&
                   _rooms.RoomContainsBoundaryCell(
                       CurrentRoom,
                       playerCell)
                ? CurrentRoom
                : null;
        }

        private void SetDisplayRoom(Room room)
        {
            if (ReferenceEquals(_displayRoom, room))
                return;

            _displayRoom = room;
            _visibleCells.Clear();
            if (room != null)
            {
                RoomRoofPadding.ExpandRoofAndMask(
                    room.InteriorCells,
                    _visibleCells);
            }
            _maskGeometryDirty = true;
        }

        private void UpdateMask()
        {
            if (_maskRenderer == null || _maskMaterial == null)
                return;

            bool visible = _displayRoom != null && _outsideAlpha > 0f;
            _maskRenderer.enabled = visible;
            if (!visible)
                return;

            if (_camera == null)
                _camera = Camera.main;
            if (_camera == null)
                return;

            BoundsInt cameraBounds = GetCameraCellBounds(_camera);
            if (_maskGeometryDirty || cameraBounds != _lastCameraBounds)
            {
                _lastCameraBounds = cameraBounds;
                _maskGeometryDirty = false;
                RoomVisibilityMaskBuilder.Build(
                    cameraBounds,
                    _visibleCells,
                    _vertices,
                    _triangles);
                _maskMesh.Clear();
                _maskMesh.SetVertices(_vertices);
                _maskMesh.SetTriangles(_triangles, 0);
                _maskMesh.RecalculateBounds();
            }

            Color color = Color.black;
            color.a = _outsideAlpha;
            _maskMaterial.color = color;
        }

        private static BoundsInt GetCameraCellBounds(Camera camera)
        {
            float halfHeight = camera.orthographic
                ? camera.orthographicSize
                : 20f;
            float halfWidth = halfHeight * camera.aspect;
            Vector3 position = camera.transform.position;
            int xMin = Mathf.FloorToInt(position.x - halfWidth) -
                       CameraPaddingCells;
            int xMax = Mathf.CeilToInt(position.x + halfWidth) +
                       CameraPaddingCells;
            int yMin = Mathf.FloorToInt(position.y - halfHeight) -
                       CameraPaddingCells;
            int yMax = Mathf.CeilToInt(position.y + halfHeight) +
                       CameraPaddingCells;
            return new BoundsInt(
                xMin,
                yMin,
                0,
                xMax - xMin,
                yMax - yMin,
                1);
        }

        private void CreateMaskRenderer()
        {
            GameObject mask = new("Room Outside Mask");
            mask.transform.SetParent(transform, false);
            MeshFilter filter = mask.AddComponent<MeshFilter>();
            _maskRenderer = mask.AddComponent<MeshRenderer>();
            _maskRenderer.sortingLayerID = 0;
            _maskRenderer.sortingOrder = OutsideMaskSortingOrder;

            _maskMesh = new Mesh
            {
                name = "Room Outside Mask Mesh",
                indexFormat = IndexFormat.UInt32
            };
            _maskMesh.MarkDynamic();
            filter.sharedMesh = _maskMesh;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError(
                    "Room visibility requires the built-in Sprites/Default shader.",
                    this);
                mask.SetActive(false);
                return;
            }

            _maskMaterial = new Material(shader)
            {
                name = "Room Outside Mask Material",
                color = new Color(0f, 0f, 0f, 0f)
            };
            _maskRenderer.sharedMaterial = _maskMaterial;
            _maskRenderer.enabled = false;
        }

        private void CachePlayerRenderers()
        {
            _playerRenderers.Clear();
            if (_player == null)
                return;

            foreach (SpriteRenderer renderer in
                     _player.GetComponentsInChildren<SpriteRenderer>(true))
            {
                _playerRenderers.Add(new PlayerRendererState(
                    renderer,
                    renderer.sortingLayerID,
                    renderer.sortingOrder));
            }
        }

        private void SetPlayerRaised(bool raised)
        {
            if (_playerRaised == raised)
                return;
            _playerRaised = raised;
            for (int i = 0; i < _playerRenderers.Count; i++)
            {
                PlayerRendererState state = _playerRenderers[i];
                if (state.Renderer == null)
                    continue;

                state.Renderer.sortingLayerID = raised
                    ? 0
                    : state.SortingLayerId;
                state.Renderer.sortingOrder = raised
                    ? PlayerSortingOrder
                    : state.SortingOrder;
            }
        }

        private void OnDestroy()
        {
            SetPlayerRaised(false);
            if (_rooms != null)
                _rooms.SetActiveRoomRoofVisibility(null, 1f);
            if (_maskMesh != null)
                Destroy(_maskMesh);
            if (_maskMaterial != null)
                Destroy(_maskMaterial);
        }

        private readonly struct PlayerRendererState
        {
            public readonly SpriteRenderer Renderer;
            public readonly int SortingLayerId;
            public readonly int SortingOrder;

            public PlayerRendererState(
                SpriteRenderer renderer,
                int sortingLayerId,
                int sortingOrder)
            {
                Renderer = renderer;
                SortingLayerId = sortingLayerId;
                SortingOrder = sortingOrder;
            }
        }
    }
}
