using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    public sealed class Room
    {
        private readonly RoomChunkSegment[] _segments;

        public Room(
            long id,
            Vector3Int[] interiorCells,
            Vector3Int[] boundaryCells,
            RoomChunkSegment[] segments)
        {
            Id = id;
            InteriorCells = interiorCells ?? Array.Empty<Vector3Int>();
            BoundaryCells = boundaryCells ?? Array.Empty<Vector3Int>();
            _segments = segments ?? Array.Empty<RoomChunkSegment>();

            if (InteriorCells.Count == 0)
            {
                Bounds = default;
                return;
            }

            Vector3Int minimum = InteriorCells[0];
            Vector3Int maximum = minimum;
            for (int i = 1; i < InteriorCells.Count; i++)
            {
                minimum = Vector3Int.Min(minimum, InteriorCells[i]);
                maximum = Vector3Int.Max(maximum, InteriorCells[i]);
            }

            Bounds = new BoundsInt(
                minimum,
                maximum - minimum + Vector3Int.one);
        }

        public long Id { get; }
        public int Area => InteriorCells.Count;
        public BoundsInt Bounds { get; }
        public IReadOnlyList<Vector3Int> InteriorCells { get; }
        public IReadOnlyList<Vector3Int> BoundaryCells { get; }
        public IReadOnlyList<RoomChunkSegment> Segments => _segments;
    }

    public sealed class RoomChunkSegment
    {
        public RoomChunkSegment(
            Vector2Int chunkPosition,
            ushort[] interiorIndices,
            ushort[] boundaryIndices)
        {
            ChunkPosition = chunkPosition;
            InteriorIndices = interiorIndices ?? Array.Empty<ushort>();
            BoundaryIndices = boundaryIndices ?? Array.Empty<ushort>();
        }

        public Room Room { get; set; }
        public Vector2Int ChunkPosition { get; }
        public IReadOnlyList<ushort> InteriorIndices { get; }
        public IReadOnlyList<ushort> BoundaryIndices { get; }
    }
}
