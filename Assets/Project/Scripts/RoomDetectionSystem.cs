using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    /// <summary>
    /// Maintains derived room topology for restored chunks. Tile mutations only
    /// enqueue work; a bounded number of flood fills is completed each frame.
    /// </summary>
    public sealed class RoomDetectionSystem : MonoBehaviour
    {
        private const string BuildingsLayerName = "Buildings";
        private const int MaximumDirtyCellsPerFrame = 128;
        private const int MaximumSeedDequeuesPerFrame = 4096;

        private static readonly Vector3Int[] CardinalDirections =
        {
            Vector3Int.left,
            Vector3Int.right,
            Vector3Int.down,
            Vector3Int.up
        };

        [Inject] private WorldData _worldData;
        [Inject] private WorldTilemapRenderer _renderer;
        [Inject] private Grid _grid;

        private readonly Dictionary<Vector2Int, Chunk> _readyChunks = new();
        private readonly Dictionary<Vector3Int, Room> _roomByInterior = new();
        private readonly Dictionary<Vector3Int, List<Room>> _roomsByBoundary = new();
        private readonly HashSet<Room> _rooms = new();
        private readonly Queue<Vector3Int> _dirtyCells = new();
        private readonly HashSet<Vector3Int> _queuedDirtyCells = new();
        private readonly Queue<Vector3Int> _pendingSeeds = new();
        private readonly HashSet<Vector3Int> _queuedSeeds = new();
        private readonly HashSet<Room> _roomsToRemove = new();
        private readonly HashSet<Vector2Int> _dirtyRoofChunks = new();
        private readonly Dictionary<Vector3Int, int> _roofCellReferences = new();
        private readonly HashSet<Vector3Int> _paddedRoofCells = new();
        private readonly HashSet<Vector3Int> _activeRoofFadeCells = new();
        private readonly List<WorldTilemapRenderer.CellData> _roofCells = new();
        private readonly List<BuildingRoomCluster> _buildingClusters = new();
        private readonly List<BuildingRoomTrigger> _buildingTriggers = new();
        private readonly Stack<BuildingRoomTrigger> _pooledBuildingTriggers = new();
        private readonly RoomFloodFill _floodFill = new();
        private Room _activeRoofRoom;
        private float _activeRoofAlpha = 1f;
        private bool _buildingCollidersDirty;
        private long _nextRoomId;

        public IReadOnlyCollection<Room> Rooms => _rooms;
        public IReadOnlyList<BuildingRoomTrigger> BuildingTriggers =>
            _buildingTriggers;

        private void Update()
        {
            ProcessPendingWork();
        }

        public bool TryGetRoom(Vector3Int worldCell, out Room room)
        {
            worldCell.z = 0;
            return _roomByInterior.TryGetValue(worldCell, out room);
        }

        public bool RoomContainsBoundaryCell(Room room, Vector3Int worldCell)
        {
            if (room == null ||
                !_roomsByBoundary.TryGetValue(worldCell, out List<Room> rooms))
            {
                return false;
            }

            return rooms.Contains(room);
        }

        public void SetActiveRoomRoofVisibility(Room room, float alpha)
        {
            if (room != null && !_rooms.Contains(room))
                room = null;

            alpha = Mathf.Clamp01(alpha);
            bool roomChanged = !ReferenceEquals(_activeRoofRoom, room);
            if (roomChanged)
            {
                // Restoring every referenced roof cell when leaving prevents
                // load-time or topology rebuilds from leaving behind a hidden
                // tile that was not present in the cached active footprint.
                ApplyAndQueueRoofFadeColors(
                    room == null
                        ? _roofCellReferences.Keys
                        : _activeRoofFadeCells,
                    1f);
                _activeRoofFadeCells.Clear();
                _activeRoofRoom = room;
                if (room != null)
                {
                    RoomRoofPadding.ExpandRoofAndMask(
                        room.InteriorCells,
                        _activeRoofFadeCells);
                }
            }

            // A direct transition between adjacent rooms keeps the same target
            // alpha (normally zero). The new room still needs that alpha
            // applied even though the numeric value did not change.
            if (!roomChanged &&
                Mathf.Approximately(_activeRoofAlpha, alpha))
            {
                return;
            }

            _activeRoofAlpha = alpha;
            ApplyAndQueueRoofFadeColors(_activeRoofFadeCells, alpha);
        }

        public void NotifyStructuralTileChanged(Vector3Int worldCell)
        {
            worldCell.z = 0;
            if (_queuedDirtyCells.Add(worldCell))
                _dirtyCells.Enqueue(worldCell);
        }

        public void NotifyChunkRestored(Chunk chunk)
        {
            if (chunk == null)
                return;

            _readyChunks[chunk.Position] = chunk;
            _dirtyRoofChunks.Add(chunk.Position);
            int startX = chunk.Position.x * ChunkBuildResult.ChunkSize;
            int startY = chunk.Position.y * ChunkBuildResult.ChunkSize;
            for (int y = 0; y < ChunkBuildResult.ChunkSize; y++)
            {
                for (int x = 0; x < ChunkBuildResult.ChunkSize; x++)
                    EnqueueSeed(new Vector3Int(startX + x, startY + y, 0));
            }
        }

        public void NotifyChunkUnloading(Chunk chunk)
        {
            if (chunk == null ||
                !_readyChunks.Remove(chunk.Position))
            {
                return;
            }

            _roomsToRemove.Clear();
            for (int i = 0; i < chunk.Rooms.Count; i++)
                _roomsToRemove.Add(chunk.Rooms[i].Room);
            foreach (Room room in _roomsToRemove)
                RemoveRoom(room);
            chunk.ClearRoomSegments();

            int startX = chunk.Position.x * ChunkBuildResult.ChunkSize;
            int startY = chunk.Position.y * ChunkBuildResult.ChunkSize;
            int size = ChunkBuildResult.ChunkSize;
            for (int offset = 0; offset < size; offset++)
            {
                EnqueueSeed(new Vector3Int(startX - 1, startY + offset, 0));
                EnqueueSeed(new Vector3Int(startX + size, startY + offset, 0));
                EnqueueSeed(new Vector3Int(startX + offset, startY - 1, 0));
                EnqueueSeed(new Vector3Int(startX + offset, startY + size, 0));
            }

            _dirtyRoofChunks.Remove(chunk.Position);
        }

        public void ProcessPendingWork()
        {
            if (_dirtyCells.Count == 0 &&
                _pendingSeeds.Count == 0 &&
                _dirtyRoofChunks.Count == 0 &&
                !_buildingCollidersDirty)
            {
                return;
            }

            ProcessDirtyCells(MaximumDirtyCellsPerFrame);
            ProcessFloodFills(Mathf.Max(1, _worldData.roomFloodFillsPerFrame));
            ApplyDirtyRoofs();
            RebuildBuildingColliders();
        }

        private void ProcessDirtyCells(int budget)
        {
            while (budget-- > 0 && _dirtyCells.Count > 0)
            {
                Vector3Int dirty = _dirtyCells.Dequeue();
                _queuedDirtyCells.Remove(dirty);
                InvalidateRoomsTouching(dirty);
                for (int i = 0; i < CardinalDirections.Length; i++)
                    EnqueueSeed(dirty + CardinalDirections[i]);
            }
        }

        private void ProcessFloodFills(int floodFillBudget)
        {
            int completedFloodFills = 0;
            int dequeued = 0;
            while (completedFloodFills < floodFillBudget &&
                   dequeued++ < MaximumSeedDequeuesPerFrame &&
                   _pendingSeeds.Count > 0)
            {
                Vector3Int seed = _pendingSeeds.Dequeue();
                if (!_queuedSeeds.Remove(seed) ||
                    _roomByInterior.ContainsKey(seed) ||
                    Classify(seed) != RoomCellKind.Open)
                {
                    continue;
                }

                RoomFloodResult result = _floodFill.Evaluate(
                    seed,
                    Mathf.Max(1, _worldData.maximumRoomArea),
                    Classify);
                completedFloodFills++;

                for (int i = 0; i < result.Interior.Count; i++)
                    _queuedSeeds.Remove(result.Interior[i]);

                if (result.Status == RoomFloodStatus.Enclosed)
                    AddRoom(result);
            }
        }

        private RoomCellKind Classify(Vector3Int worldCell)
        {
            worldCell.z = 0;
            Vector2Int chunkPosition = WorldToChunkPosition(worldCell);
            if (!_readyChunks.ContainsKey(chunkPosition))
                return RoomCellKind.Unavailable;

            return _renderer.TryGetTileData(
                       PersistentTileLayer.Wall,
                       worldCell,
                       out TileData tile) &&
                   tile.EnclosesRoom
                ? RoomCellKind.Boundary
                : RoomCellKind.Open;
        }

        private void InvalidateRoomsTouching(Vector3Int cell)
        {
            _roomsToRemove.Clear();
            CollectRoomsAt(cell);
            for (int i = 0; i < CardinalDirections.Length; i++)
                CollectRoomsAt(cell + CardinalDirections[i]);

            foreach (Room room in _roomsToRemove)
                RemoveRoom(room);
        }

        private void CollectRoomsAt(Vector3Int cell)
        {
            if (_roomByInterior.TryGetValue(cell, out Room interiorRoom))
                _roomsToRemove.Add(interiorRoom);
            if (!_roomsByBoundary.TryGetValue(cell, out List<Room> boundaryRooms))
                return;
            for (int i = 0; i < boundaryRooms.Count; i++)
                _roomsToRemove.Add(boundaryRooms[i]);
        }

        private void AddRoom(RoomFloodResult result)
        {
            Vector3Int[] interior = new Vector3Int[result.Interior.Count];
            for (int i = 0; i < interior.Length; i++)
                interior[i] = result.Interior[i];

            Vector3Int[] boundary = new Vector3Int[result.Boundary.Count];
            result.MutableBoundary.CopyTo(boundary);

            Dictionary<Vector2Int, SegmentBuilder> builders = new();
            for (int i = 0; i < interior.Length; i++)
                GetBuilder(builders, interior[i]).Interior.Add(ToLocalIndex(interior[i]));
            for (int i = 0; i < boundary.Length; i++)
                GetBuilder(builders, boundary[i]).Boundary.Add(ToLocalIndex(boundary[i]));

            RoomChunkSegment[] segments = new RoomChunkSegment[builders.Count];
            int segmentIndex = 0;
            foreach (KeyValuePair<Vector2Int, SegmentBuilder> pair in builders)
            {
                segments[segmentIndex++] = new RoomChunkSegment(
                    pair.Key,
                    pair.Value.Interior.ToArray(),
                    pair.Value.Boundary.ToArray());
            }

            Room room = new(++_nextRoomId, interior, boundary, segments);
            for (int i = 0; i < segments.Length; i++)
            {
                RoomChunkSegment segment = segments[i];
                segment.Room = room;
                if (_readyChunks.TryGetValue(segment.ChunkPosition, out Chunk chunk))
                {
                    chunk.AddRoomSegment(segment);
                    _dirtyRoofChunks.Add(segment.ChunkPosition);
                }
            }

            for (int i = 0; i < interior.Length; i++)
                _roomByInterior[interior[i]] = room;
            for (int i = 0; i < boundary.Length; i++)
            {
                if (!_roomsByBoundary.TryGetValue(boundary[i], out List<Room> list))
                {
                    list = new List<Room>(2);
                    _roomsByBoundary.Add(boundary[i], list);
                }
                list.Add(room);
            }
            _rooms.Add(room);
            UpdateRoofCoverage(room, 1);
            _buildingCollidersDirty = true;
        }

        private void RemoveRoom(Room room)
        {
            if (room == null || !_rooms.Remove(room))
                return;

            if (ReferenceEquals(_activeRoofRoom, room))
                SetActiveRoomRoofVisibility(null, 1f);
            UpdateRoofCoverage(room, -1);
            for (int i = 0; i < room.InteriorCells.Count; i++)
                _roomByInterior.Remove(room.InteriorCells[i]);
            for (int i = 0; i < room.BoundaryCells.Count; i++)
            {
                Vector3Int cell = room.BoundaryCells[i];
                if (!_roomsByBoundary.TryGetValue(cell, out List<Room> list))
                    continue;
                list.Remove(room);
                if (list.Count == 0)
                    _roomsByBoundary.Remove(cell);
            }

            for (int i = 0; i < room.Segments.Count; i++)
            {
                RoomChunkSegment segment = room.Segments[i];
                if (_readyChunks.TryGetValue(segment.ChunkPosition, out Chunk chunk))
                    chunk.RemoveRoomSegment(segment);
                _dirtyRoofChunks.Add(segment.ChunkPosition);
            }
            _buildingCollidersDirty = true;
        }

        private void RebuildBuildingColliders()
        {
            if (!_buildingCollidersDirty)
                return;

            _buildingCollidersDirty = false;
            BuildingRoomClusterer.Build(
                _rooms,
                _worldData.buildingRoomConnectionDistance,
                _buildingClusters);

            int buildingLayer = LayerMask.NameToLayer(BuildingsLayerName);
            if (buildingLayer < 0)
            {
                Debug.LogError(
                    $"Required Unity layer '{BuildingsLayerName}' is missing.",
                    this);
                return;
            }

            while (_buildingTriggers.Count > _buildingClusters.Count)
            {
                int last = _buildingTriggers.Count - 1;
                BuildingRoomTrigger trigger = _buildingTriggers[last];
                _buildingTriggers.RemoveAt(last);
                trigger.Release();
                _pooledBuildingTriggers.Push(trigger);
            }

            for (int i = 0; i < _buildingClusters.Count; i++)
            {
                BuildingRoomTrigger trigger;
                if (i < _buildingTriggers.Count)
                {
                    trigger = _buildingTriggers[i];
                }
                else
                {
                    trigger = AcquireBuildingTrigger();
                    _buildingTriggers.Add(trigger);
                }

                trigger.Configure(
                    _buildingClusters[i],
                    i + 1,
                    buildingLayer,
                    _grid);
            }
        }

        private BuildingRoomTrigger AcquireBuildingTrigger()
        {
            if (_pooledBuildingTriggers.Count > 0)
                return _pooledBuildingTriggers.Pop();

            GameObject building = new("Building");
            building.transform.SetParent(transform, true);
            return building.AddComponent<BuildingRoomTrigger>();
        }

        private void ApplyDirtyRoofs()
        {
            if (_dirtyRoofChunks.Count == 0)
                return;

            foreach (Vector2Int chunkPosition in _dirtyRoofChunks)
            {
                if (!_readyChunks.TryGetValue(chunkPosition, out Chunk chunk))
                    continue;

                _roofCells.Clear();
                TileData roof = _worldData.roomRoofTile;
                if (roof != null && roof.HasVisual)
                {
                    int startX = chunkPosition.x * ChunkBuildResult.ChunkSize;
                    int startY = chunkPosition.y * ChunkBuildResult.ChunkSize;
                    for (int localY = 0;
                         localY < ChunkBuildResult.ChunkSize;
                         localY++)
                    {
                        for (int localX = 0;
                             localX < ChunkBuildResult.ChunkSize;
                             localX++)
                        {
                            Vector3Int worldCell = new(
                                startX + localX,
                                startY + localY,
                                0);
                            if (!_roofCellReferences.ContainsKey(worldCell))
                                continue;

                            Color color = roof.Color;
                            if (_activeRoofFadeCells.Contains(worldCell))
                            {
                                // A fully hidden roof is omitted instead of
                                // relying on per-tile alpha. Some tile
                                // materials and delayed bakes do not preserve
                                // vertex alpha consistently.
                                if (_activeRoofAlpha <= 0.001f)
                                    continue;
                                color.a *= _activeRoofAlpha;
                            }
                            _roofCells.Add(new WorldTilemapRenderer.CellData(
                                worldCell,
                                roof,
                                color));
                        }
                    }
                }

                _renderer.SetRoofTiles(chunkPosition, _roofCells);
            }
            _dirtyRoofChunks.Clear();
        }

        private void UpdateRoofCoverage(Room room, int delta)
        {
            RoomRoofPadding.ExpandRoofAndMask(
                room.InteriorCells,
                _paddedRoofCells);
            foreach (Vector3Int cell in _paddedRoofCells)
            {
                _roofCellReferences.TryGetValue(cell, out int references);
                references += delta;
                if (references <= 0)
                    _roofCellReferences.Remove(cell);
                else
                    _roofCellReferences[cell] = references;
                _dirtyRoofChunks.Add(WorldToChunkPosition(cell));
            }
        }

        private void ApplyRoofFadeColors(
            IEnumerable<Vector3Int> cells,
            float alpha)
        {
            TileData roof = _worldData.roomRoofTile;
            if (roof == null)
                return;

            Color color = roof.Color;
            color.a *= Mathf.Clamp01(alpha);
            foreach (Vector3Int worldCell in cells)
            {
                if (!_roofCellReferences.ContainsKey(worldCell))
                    continue;
                _renderer.SetRoofColor(worldCell, color);
            }
        }

        private void ApplyAndQueueRoofFadeColors(
            IEnumerable<Vector3Int> cells,
            float alpha)
        {
            ApplyRoofFadeColors(cells, alpha);

            // SetRoofColor updates the currently baked tile immediately, while
            // scheduling the owning chunks makes the logical roof rebuild use
            // the same active-room state. This prevents a pending load-time or
            // auto-tile bake from restoring the old visibility afterward.
            foreach (Vector3Int worldCell in cells)
            {
                if (_roofCellReferences.ContainsKey(worldCell))
                    _dirtyRoofChunks.Add(WorldToChunkPosition(worldCell));
            }
        }

        private void EnqueueSeed(Vector3Int worldCell)
        {
            worldCell.z = 0;
            if (_queuedSeeds.Add(worldCell))
                _pendingSeeds.Enqueue(worldCell);
        }

        private static SegmentBuilder GetBuilder(
            Dictionary<Vector2Int, SegmentBuilder> builders,
            Vector3Int worldCell)
        {
            Vector2Int chunkPosition = WorldToChunkPosition(worldCell);
            if (!builders.TryGetValue(chunkPosition, out SegmentBuilder builder))
            {
                builder = new SegmentBuilder();
                builders.Add(chunkPosition, builder);
            }
            return builder;
        }

        private static Vector2Int WorldToChunkPosition(Vector3Int worldCell)
        {
            int size = ChunkBuildResult.ChunkSize;
            return new Vector2Int(
                FloorDiv(worldCell.x, size),
                FloorDiv(worldCell.y, size));
        }

        private static ushort ToLocalIndex(Vector3Int worldCell)
        {
            int size = ChunkBuildResult.ChunkSize;
            int localX = worldCell.x - FloorDiv(worldCell.x, size) * size;
            int localY = worldCell.y - FloorDiv(worldCell.y, size) * size;
            return (ushort)(localX + localY * size);
        }

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }

        private sealed class SegmentBuilder
        {
            public readonly List<ushort> Interior = new();
            public readonly List<ushort> Boundary = new();
        }
    }
}
