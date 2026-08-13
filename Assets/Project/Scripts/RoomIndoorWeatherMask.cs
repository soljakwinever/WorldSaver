using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using Project.Scripts.DataTypes;
using Project.Scripts.TimeAndWeather;
using UnityEngine;

namespace Project.Scripts
{
    public sealed class RoomIndoorWeatherMask : IIndoorWeatherMask,
        IIndoorLocationService
    {
        private readonly RoomDetectionSystem _rooms;
        private readonly Grid _grid;
        private readonly PlayerDataController _player;
        private Room _lastViewerRoom;

        public RoomIndoorWeatherMask(
            RoomDetectionSystem rooms,
            Grid grid,
            PlayerDataController player)
        {
            _rooms = rooms;
            _grid = grid;
            _player = player;
        }

        public bool IsViewerIndoors
        {
            get
            {
                if (_player == null || _rooms == null || _grid == null)
                    return false;

                Vector3Int cell =
                    _grid.WorldToCell(_player.transform.position);
                if (_rooms.TryGetRoom(cell, out Room room))
                {
                    _lastViewerRoom = room;
                    return true;
                }

                if (_lastViewerRoom != null &&
                    _rooms.RoomContainsBoundaryCell(
                        _lastViewerRoom,
                        cell))
                {
                    return true;
                }

                _lastViewerRoom = null;
                return false;
            }
        }

        public bool TryGetViewerWorldPosition(out Vector2 worldPosition)
        {
            if (_player == null)
            {
                worldPosition = default;
                return false;
            }

            worldPosition = _player.transform.position;
            return true;
        }

        public bool IsWorldPositionIndoors(Vector2 worldPosition)
        {
            if (_rooms == null || _grid == null)
                return false;

            Vector3Int cell = _grid.WorldToCell(worldPosition);
            return _rooms.TryGetRoom(cell, out _);
        }

        public bool TryFindNearestIndoorPosition(Vector2 origin,
            Vector2 areaCenter, float areaRadius, out Vector2 position)
        {
            position = default;
            if (_rooms == null) return false;
            float radiusSquared = Mathf.Max(0f, areaRadius) *
                                  Mathf.Max(0f, areaRadius);
            float best = float.PositiveInfinity;
            foreach (Room room in _rooms.Rooms)
                foreach (Vector3Int cell in room.InteriorCells)
                {
                    Vector2 candidate = new(cell.x + 0.5f, cell.y + 0.5f);
                    if ((candidate - areaCenter).sqrMagnitude > radiusSquared)
                        continue;
                    float distance = (candidate - origin).sqrMagnitude;
                    if (distance >= best) continue;
                    best = distance;
                    position = candidate;
                }
            return !float.IsPositiveInfinity(best);
        }
    }
}
