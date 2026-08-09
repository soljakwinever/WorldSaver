using System;
using System.Collections.Generic;
using Project.Scripts.Bus;
using Project.Scripts.Core;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Project.Scripts.UI
{
    /// <summary>
    /// UI Toolkit world map backed by one tiny texture per visited chunk.
    /// Terrain cells are flattened to ground/water/wall color before upload.
    /// </summary>
    public sealed class WorldMapScreenController : IDisposable
    {
        private const float PixelsPerCell = 4f;
        private const float ChunkPixels =
            ChunkBuildResult.ChunkSize * PixelsPerCell;
        private const int CellCount =
            ChunkBuildResult.ChunkSize * ChunkBuildResult.ChunkSize;
        private const int NameRegionChunks = 4;
        private const int RegionSampleAxis = 4;
        private const float NameLabelWidth = 240f;
        private static readonly Color32 ReservationColor =
            new(156, 108, 61, byte.MaxValue);

        private readonly MapSignalBus _signals;
        private readonly Chunkloader _chunkloader;
        private readonly WorldTilemapRenderer _renderer;
        private readonly PlayerDataController _player;
        private readonly WorldGeneration _worldGeneration;
        private readonly Dictionary<string, Dictionary<Vector2Int, ChunkMap>>
            _planes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<Vector2Int, RegionSite>>
            _regionSites = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Chunk> _loadedChunks = new();
        private readonly Color32[] _colorBuffer = new Color32[CellCount];
        private readonly VisualElement _screen;
        private readonly VisualElement _viewport;
        private readonly VisualElement _canvas;
        private readonly VisualElement _playerMarker;
        private readonly Label _planeLabel;

        private string _displayedPlaneId;
        private Vector2 _pan;
        private Vector2 _lastPointerPosition;
        private int _dragPointerId = -1;
        private bool _visible;
        private bool _centerWhenReady;

        public bool IsVisible => _visible;

        private sealed class ChunkMap
        {
            public Texture2D Texture;
            public Image View;
        }

        private sealed class RegionSite
        {
            public Vector2Int Coordinate;
            public Vector2 Position;
            public BiomeData Biome;
            public Label NameView;
        }

        public WorldMapScreenController(
            VisualElement root,
            MapSignalBus signals,
            Chunkloader chunkloader,
            WorldTilemapRenderer renderer,
            PlayerDataController player,
            WorldGeneration worldGeneration)
        {
            _signals = signals;
            _chunkloader = chunkloader;
            _renderer = renderer;
            _player = player;
            _worldGeneration = worldGeneration;

            _screen = BuildScreen(out _viewport, out _canvas,
                out _playerMarker, out _planeLabel);
            root.Add(_screen);
            _screen.BringToFront();
            SetVisible(false);

            _viewport.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _viewport.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _viewport.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _viewport.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            _viewport.RegisterCallback<GeometryChangedEvent>(OnViewportGeometryChanged);
            _signals.ChunkLoaded += OnChunkLoaded;
            _signals.NavigationCellChanged += OnNavigationCellChanged;
            _signals.NavigationChunkChanged += OnNavigationChunkChanged;
            TileReservationSystem.ReservationsChanged += OnReservationsChanged;
            SnapshotLoadedChunks();
        }

        public void Dispose()
        {
            _signals.ChunkLoaded -= OnChunkLoaded;
            _signals.NavigationCellChanged -= OnNavigationCellChanged;
            _signals.NavigationChunkChanged -= OnNavigationChunkChanged;
            TileReservationSystem.ReservationsChanged -= OnReservationsChanged;
            foreach (Dictionary<Vector2Int, ChunkMap> plane in _planes.Values)
            foreach (ChunkMap chunk in plane.Values)
                if (chunk.Texture != null)
                    UnityEngine.Object.Destroy(chunk.Texture);
            _planes.Clear();
            _regionSites.Clear();
            _screen.RemoveFromHierarchy();
        }

        public void PollKeyboard()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.mKey.wasPressedThisFrame)
                    SetVisible(!_visible);
                else if (_visible && keyboard.escapeKey.wasPressedThisFrame)
                    SetVisible(false);
            }

            if (!_visible)
                return;

            string planeId = CurrentPlaneId;
            if (!string.Equals(
                    planeId,
                    _displayedPlaneId,
                    StringComparison.OrdinalIgnoreCase))
            {
                ShowPlane(planeId);
                CenterOnPlayer();
            }
            UpdatePlayerMarker();
        }

        private string CurrentPlaneId =>
            string.IsNullOrWhiteSpace(_chunkloader.CurrentPlaneId)
                ? "World"
                : _chunkloader.CurrentPlaneId;

        private void SetVisible(bool visible)
        {
            _visible = visible;
            _screen.style.display = visible
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            if (!visible)
            {
                if (_dragPointerId >= 0 &&
                    _viewport.HasPointerCapture(_dragPointerId))
                    _viewport.ReleasePointer(_dragPointerId);
                _dragPointerId = -1;
                return;
            }

            SnapshotLoadedChunks();
            ShowPlane(CurrentPlaneId);
            _screen.BringToFront();
            _centerWhenReady = true;
            CenterOnPlayer();
        }

        private void SnapshotLoadedChunks()
        {
            _chunkloader.CopyLoadedChunks(_loadedChunks);
            string planeId = CurrentPlaneId;
            foreach (Chunk chunk in _loadedChunks)
                SnapshotChunk(planeId, chunk.Position);
        }

        private void OnChunkLoaded(Vector2Int position, IChunk _)
        {
            string planeId = CurrentPlaneId;
            if (SnapshotChunk(planeId, position) && _visible &&
                string.Equals(planeId, _displayedPlaneId,
                    StringComparison.OrdinalIgnoreCase))
            {
                AddChunkView(position, GetPlane(planeId)[position]);
            }
        }

        private void OnNavigationCellChanged(Vector2Int worldCell)
        {
            Vector2Int chunk = new(
                Mathf.FloorToInt((float)worldCell.x / ChunkBuildResult.ChunkSize),
                Mathf.FloorToInt((float)worldCell.y / ChunkBuildResult.ChunkSize));
            RefreshVisitedChunk(chunk);
        }

        private void OnNavigationChunkChanged(Vector2Int chunkPosition) =>
            RefreshVisitedChunk(chunkPosition);

        private void RefreshVisitedChunk(Vector2Int position)
        {
            string planeId = CurrentPlaneId;
            if (_planes.TryGetValue(planeId, out var plane) &&
                plane.ContainsKey(position))
                SnapshotChunk(planeId, position);
        }

        private void OnReservationsChanged(RectInt area)
        {
            if (area.width <= 0 || area.height <= 0)
                return;

            int chunkSize = ChunkBuildResult.ChunkSize;
            int xMin = Mathf.FloorToInt((float)area.xMin / chunkSize);
            int yMin = Mathf.FloorToInt((float)area.yMin / chunkSize);
            int xMax = Mathf.FloorToInt((float)(area.xMax - 1) / chunkSize);
            int yMax = Mathf.FloorToInt((float)(area.yMax - 1) / chunkSize);
            for (int y = yMin; y <= yMax; y++)
            for (int x = xMin; x <= xMax; x++)
                RefreshVisitedChunk(new Vector2Int(x, y));
        }

        private bool SnapshotChunk(string planeId, Vector2Int position)
        {
            if (!_renderer.CopyMapColors(position, _colorBuffer))
                return false;

            EnsureNearbyRegionSites(planeId, position);
            OverlayVoronoiBoundaries(planeId, position);
            OverlayReservations(position);

            Dictionary<Vector2Int, ChunkMap> plane = GetPlane(planeId);
            if (!plane.TryGetValue(position, out ChunkMap map))
            {
                map = new ChunkMap
                {
                    Texture = new Texture2D(
                        ChunkBuildResult.ChunkSize,
                        ChunkBuildResult.ChunkSize,
                        TextureFormat.RGBA32,
                        mipChain: false,
                        linear: true)
                    {
                        name = $"Visited map {planeId} {position.x},{position.y}",
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                        hideFlags = HideFlags.HideAndDontSave
                    }
                };
                plane.Add(position, map);
            }

            map.Texture.SetPixels32(_colorBuffer);
            map.Texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return true;
        }

        private void OverlayReservations(Vector2Int chunkPosition)
        {
            int chunkSize = ChunkBuildResult.ChunkSize;
            Vector2Int origin = chunkPosition * chunkSize;
            for (int y = 0; y < chunkSize; y++)
            for (int x = 0; x < chunkSize; x++)
            {
                if (TileReservationSystem.IsReserved(
                        origin + new Vector2Int(x, y)))
                {
                    _colorBuffer[y * chunkSize + x] = ReservationColor;
                }
            }
        }

        private void EnsureNearbyRegionSites(
            string planeId,
            Vector2Int chunkPosition)
        {
            if (_worldGeneration == null)
                return;

            Vector2Int region = ChunkToNameRegion(chunkPosition);
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
                GetOrCreateRegionSite(planeId, region + new Vector2Int(x, y));
        }

        private RegionSite GetOrCreateRegionSite(
            string planeId,
            Vector2Int coordinate)
        {
            if (!_regionSites.TryGetValue(planeId, out var sites))
            {
                sites = new Dictionary<Vector2Int, RegionSite>();
                _regionSites.Add(planeId, sites);
            }
            if (sites.TryGetValue(coordinate, out RegionSite existing))
                return existing;

            int regionCells = NameRegionChunks * ChunkBuildResult.ChunkSize;
            Vector2Int origin = coordinate * regionCells;
            Dictionary<BiomeData, List<Vector2>> samples = new();
            for (int y = 0; y < RegionSampleAxis; y++)
            for (int x = 0; x < RegionSampleAxis; x++)
            {
                Vector2 sample = origin + new Vector2(
                    (x + .5f) * regionCells / RegionSampleAxis,
                    (y + .5f) * regionCells / RegionSampleAxis);
                BiomeData biome = _worldGeneration.GetTerrainSample(
                    Mathf.FloorToInt(sample.x),
                    Mathf.FloorToInt(sample.y)).biome;
                if (biome == null)
                    continue;
                if (!samples.TryGetValue(biome, out List<Vector2> positions))
                {
                    positions = new List<Vector2>();
                    samples.Add(biome, positions);
                }
                positions.Add(sample);
            }

            BiomeData dominant = null;
            List<Vector2> dominantSamples = null;
            foreach (KeyValuePair<BiomeData, List<Vector2>> pair in samples)
            {
                if (dominantSamples == null ||
                    pair.Value.Count > dominantSamples.Count ||
                    pair.Value.Count == dominantSamples.Count &&
                    string.CompareOrdinal(pair.Key.biomeName,
                        dominant.biomeName) < 0)
                {
                    dominant = pair.Key;
                    dominantSamples = pair.Value;
                }
            }

            Vector2 position = origin + Vector2.one * (regionCells * .5f);
            if (dominantSamples != null && dominantSamples.Count > 0)
            {
                Vector2 centroid = Vector2.zero;
                foreach (Vector2 sample in dominantSamples)
                    centroid += sample;
                centroid /= dominantSamples.Count;

                // Use the sampled point nearest the centroid so the label's
                // Voronoi site is guaranteed to remain inside its biome.
                position = dominantSamples[0];
                float bestDistance = (position - centroid).sqrMagnitude;
                for (int i = 1; i < dominantSamples.Count; i++)
                {
                    float distance =
                        (dominantSamples[i] - centroid).sqrMagnitude;
                    if (distance < bestDistance)
                    {
                        position = dominantSamples[i];
                        bestDistance = distance;
                    }
                }
            }

            RegionSite site = new()
            {
                Coordinate = coordinate,
                Position = position,
                Biome = dominant
            };
            sites.Add(coordinate, site);
            return site;
        }

        private void OverlayVoronoiBoundaries(
            string planeId,
            Vector2Int chunkPosition)
        {
            if (!_regionSites.TryGetValue(planeId, out var sites) ||
                sites.Count == 0)
                return;

            int chunkSize = ChunkBuildResult.ChunkSize;
            Vector2Int origin = chunkPosition * chunkSize;
            for (int y = 0; y < chunkSize; y++)
            for (int x = 0; x < chunkSize; x++)
            {
                Vector2 cell = origin + new Vector2(x + .5f, y + .5f);
                Vector2Int owner = FindNearestSite(cell, sites);
                bool boundary =
                    FindNearestSite(cell + Vector2.right, sites) != owner ||
                    FindNearestSite(cell + Vector2.up, sites) != owner;
                if (!boundary)
                    continue;

                int index = y * chunkSize + x;
                Color original = _colorBuffer[index];
                Color boundaryColor = new(.94f, .78f, .36f, 1f);
                _colorBuffer[index] = (Color32)Color.Lerp(
                    original,
                    boundaryColor,
                    .72f);
            }
        }

        private static Vector2Int FindNearestSite(
            Vector2 position,
            Dictionary<Vector2Int, RegionSite> sites)
        {
            Vector2Int nearest = default;
            float nearestDistance = float.MaxValue;
            int regionCells = NameRegionChunks * ChunkBuildResult.ChunkSize;
            Vector2Int region = new(
                Mathf.FloorToInt(position.x / regionCells),
                Mathf.FloorToInt(position.y / regionCells));
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                Vector2Int coordinate = region + new Vector2Int(x, y);
                if (!sites.TryGetValue(coordinate, out RegionSite site))
                    continue;
                float distance = (site.Position - position).sqrMagnitude;
                if (distance < nearestDistance ||
                    Mathf.Approximately(distance, nearestDistance) &&
                    CompareCoordinates(coordinate, nearest) < 0)
                {
                    nearest = coordinate;
                    nearestDistance = distance;
                }
            }
            return nearest;
        }

        private static int CompareCoordinates(Vector2Int left, Vector2Int right)
        {
            int x = left.x.CompareTo(right.x);
            return x != 0 ? x : left.y.CompareTo(right.y);
        }

        private Dictionary<Vector2Int, ChunkMap> GetPlane(string planeId)
        {
            if (!_planes.TryGetValue(planeId, out var plane))
            {
                plane = new Dictionary<Vector2Int, ChunkMap>();
                _planes.Add(planeId, plane);
            }
            return plane;
        }

        private void ShowPlane(string planeId)
        {
            _displayedPlaneId = planeId;
            _planeLabel.text = planeId;
            _canvas.Clear();
            foreach (KeyValuePair<Vector2Int, ChunkMap> pair in GetPlane(planeId))
                AddChunkView(pair.Key, pair.Value);
            AddRegionNameViews(planeId);
            _canvas.Add(_playerMarker);
            ApplyPan();
            UpdatePlayerMarker();
        }

        private void AddChunkView(Vector2Int position, ChunkMap map)
        {
            if (map.View != null && map.View.parent == _canvas)
                return;

            if (map.View == null)
            {
                map.View = new Image
                {
                    image = map.Texture,
                    scaleMode = ScaleMode.StretchToFill,
                    pickingMode = PickingMode.Ignore
                };
                map.View.style.position = Position.Absolute;
                map.View.style.left = position.x * ChunkPixels;
                map.View.style.top = -(position.y + 1) * ChunkPixels;
                map.View.style.width = ChunkPixels;
                map.View.style.height = ChunkPixels;
            }
            _canvas.Add(map.View);
            AddRegionNameViews(_displayedPlaneId);
            _playerMarker.BringToFront();
        }

        private void AddRegionNameViews(string planeId)
        {
            if (string.IsNullOrWhiteSpace(planeId) ||
                !_planes.TryGetValue(planeId, out var plane) ||
                !_regionSites.TryGetValue(planeId, out var sites))
                return;

            HashSet<Vector2Int> visitedRegions = new();
            foreach (Vector2Int chunk in plane.Keys)
                visitedRegions.Add(ChunkToNameRegion(chunk));

            foreach (Vector2Int region in visitedRegions)
            {
                if (!sites.TryGetValue(region, out RegionSite site) ||
                    site.Biome == null)
                    continue;
                if (site.NameView == null)
                {
                string areaName = AreaNameGenerator.Generate(
                        unchecked((int)_worldGeneration.Seed),
                        site.Biome,
                        site.Coordinate);
                if (string.IsNullOrWhiteSpace(areaName))
                        continue;

                    site.NameView = new Label(areaName)
                {
                    pickingMode = PickingMode.Ignore
                };
                    site.NameView.style.position = Position.Absolute;
                    site.NameView.style.left =
                        site.Position.x * PixelsPerCell -
                    NameLabelWidth * .5f;
                    site.NameView.style.top =
                        -site.Position.y * PixelsPerCell - 12f;
                    site.NameView.style.width = NameLabelWidth;
                    site.NameView.style.unityTextAlign = TextAnchor.MiddleCenter;
                    site.NameView.style.fontSize = 15f;
                    site.NameView.style.unityFontStyleAndWeight = FontStyle.Bold;
                    site.NameView.style.color =
                        new Color(.96f, .91f, .76f, .95f);
                    site.NameView.style.backgroundColor =
                    new Color(.025f, .04f, .065f, .55f);
                }

                if (site.NameView.parent != _canvas)
                    _canvas.Add(site.NameView);
                site.NameView.BringToFront();
            }
        }

        private static Vector2Int ChunkToNameRegion(Vector2Int chunk) => new(
            FloorDivide(chunk.x, NameRegionChunks),
            FloorDivide(chunk.y, NameRegionChunks));

        private static int FloorDivide(int value, int divisor) =>
            Mathf.FloorToInt((float)value / divisor);

        private void UpdatePlayerMarker()
        {
            if (_player == null)
                return;
            Vector3 world = _player.transform.position;
            const float markerSize = 10f;
            _playerMarker.style.left = world.x * PixelsPerCell - markerSize * .5f;
            _playerMarker.style.top = -world.y * PixelsPerCell - markerSize * .5f;
        }

        private void CenterOnPlayer()
        {
            if (_player == null ||
                _viewport.resolvedStyle.width <= 0f ||
                _viewport.resolvedStyle.height <= 0f)
                return;

            Vector3 world = _player.transform.position;
            Vector2 mapPoint = new(
                world.x * PixelsPerCell,
                -world.y * PixelsPerCell);
            _pan = new Vector2(
                _viewport.resolvedStyle.width * .5f,
                _viewport.resolvedStyle.height * .5f) - mapPoint;
            _centerWhenReady = false;
            ApplyPan();
        }

        private void OnViewportGeometryChanged(GeometryChangedEvent _)
        {
            if (_visible && _centerWhenReady)
                CenterOnPlayer();
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
                return;
            _dragPointerId = evt.pointerId;
            _lastPointerPosition = evt.position;
            _viewport.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != _dragPointerId ||
                !_viewport.HasPointerCapture(evt.pointerId))
                return;
            Vector2 position = evt.position;
            _pan += position - _lastPointerPosition;
            _lastPointerPosition = position;
            ApplyPan();
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != _dragPointerId)
                return;
            _viewport.ReleasePointer(evt.pointerId);
            _dragPointerId = -1;
            evt.StopPropagation();
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent _) =>
            _dragPointerId = -1;

        private void ApplyPan() =>
            _canvas.style.translate = new Translate(_pan.x, _pan.y);

        private static VisualElement BuildScreen(
            out VisualElement viewport,
            out VisualElement canvas,
            out VisualElement playerMarker,
            out Label planeLabel)
        {
            var screen = new VisualElement
            {
                name = "WorldMapScreen",
                pickingMode = PickingMode.Position
            };
            screen.style.position = Position.Absolute;
            screen.style.left = 0f;
            screen.style.right = 0f;
            screen.style.top = 0f;
            screen.style.bottom = 0f;
            screen.style.paddingLeft = 28f;
            screen.style.paddingRight = 28f;
            screen.style.paddingTop = 22f;
            screen.style.paddingBottom = 28f;
            screen.style.backgroundColor = new Color(0.025f, 0.04f, 0.065f, .97f);

            var header = new VisualElement { pickingMode = PickingMode.Ignore };
            header.style.height = 54f;
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            var title = new Label("WORLD MAP") { pickingMode = PickingMode.Ignore };
            title.style.fontSize = 25f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(.98f, .82f, .4f);
            planeLabel = new Label { pickingMode = PickingMode.Ignore };
            planeLabel.style.fontSize = 16f;
            planeLabel.style.color = new Color(.68f, .76f, .86f);
            var hint = new Label("Drag to explore   •   M / Esc to close")
            {
                pickingMode = PickingMode.Ignore
            };
            hint.style.color = new Color(.58f, .65f, .74f);
            header.Add(title);
            header.Add(planeLabel);
            header.Add(hint);
            screen.Add(header);

            viewport = new VisualElement
            {
                name = "WorldMapViewport",
                pickingMode = PickingMode.Position
            };
            viewport.style.flexGrow = 1f;
            viewport.style.overflow = Overflow.Hidden;
            viewport.style.backgroundColor = new Color(.045f, .06f, .085f);
            viewport.style.borderLeftWidth = 2f;
            viewport.style.borderRightWidth = 2f;
            viewport.style.borderTopWidth = 2f;
            viewport.style.borderBottomWidth = 2f;
            Color border = new(.28f, .34f, .43f);
            viewport.style.borderLeftColor = border;
            viewport.style.borderRightColor = border;
            viewport.style.borderTopColor = border;
            viewport.style.borderBottomColor = border;
            screen.Add(viewport);

            canvas = new VisualElement
            {
                name = "WorldMapCanvas",
                pickingMode = PickingMode.Ignore
            };
            canvas.style.position = Position.Absolute;
            canvas.style.left = 0f;
            canvas.style.top = 0f;
            canvas.style.width = 1f;
            canvas.style.height = 1f;
            viewport.Add(canvas);

            playerMarker = new VisualElement
            {
                name = "WorldMapPlayerMarker",
                pickingMode = PickingMode.Ignore
            };
            playerMarker.style.position = Position.Absolute;
            playerMarker.style.width = 10f;
            playerMarker.style.height = 10f;
            playerMarker.style.backgroundColor = Color.white;
            playerMarker.style.borderLeftWidth = 2f;
            playerMarker.style.borderRightWidth = 2f;
            playerMarker.style.borderTopWidth = 2f;
            playerMarker.style.borderBottomWidth = 2f;
            playerMarker.style.borderLeftColor = Color.black;
            playerMarker.style.borderRightColor = Color.black;
            playerMarker.style.borderTopColor = Color.black;
            playerMarker.style.borderBottomColor = Color.black;
            playerMarker.style.borderTopLeftRadius = 5f;
            playerMarker.style.borderTopRightRadius = 5f;
            playerMarker.style.borderBottomLeftRadius = 5f;
            playerMarker.style.borderBottomRightRadius = 5f;
            return screen;
        }
    }
}
