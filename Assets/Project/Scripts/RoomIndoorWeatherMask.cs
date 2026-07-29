using Project.Scripts.Gameplay;
using Project.Scripts.DataTypes;
using Project.Scripts.TimeAndWeather;
using UnityEngine;

namespace Project.Scripts
{
    public sealed class RoomIndoorWeatherMask : IIndoorWeatherMask
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

        public bool IsWorldPositionIndoors(Vector2 worldPosition)
        {
            if (_rooms == null || _grid == null)
                return false;

            Vector3Int cell = _grid.WorldToCell(worldPosition);
            return _rooms.TryGetRoom(cell, out _);
        }
    }
}
