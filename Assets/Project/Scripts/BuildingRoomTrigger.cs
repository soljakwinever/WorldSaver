using System.Collections.Generic;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class BuildingRoomTrigger : MonoBehaviour
    {
        private readonly List<Room> _rooms = new();
        private BoxCollider2D _collider;

        public BoundsInt CellBounds { get; private set; }
        public IReadOnlyList<Room> Rooms => _rooms;
        public BoxCollider2D Collider => _collider;

        internal void Configure(
            BuildingRoomCluster cluster,
            int buildingIndex,
            int buildingLayer,
            Grid grid)
        {
            if (_collider == null)
                _collider = GetComponent<BoxCollider2D>();

            name = $"Building {buildingIndex}";
            gameObject.layer = buildingLayer;
            CellBounds = cluster.Bounds;
            _rooms.Clear();
            for (int i = 0; i < cluster.Rooms.Count; i++)
                _rooms.Add(cluster.Rooms[i]);

            Vector3 minimum = grid.CellToWorld(CellBounds.min);
            Vector3 maximum = grid.CellToWorld(CellBounds.max);
            Vector3 center = (minimum + maximum) * 0.5f;
            center.z = 0f;
            transform.position = center;
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            _collider.offset = Vector2.zero;
            _collider.size = new Vector2(
                Mathf.Max(grid.cellSize.x, Mathf.Abs(maximum.x - minimum.x)),
                Mathf.Max(grid.cellSize.y, Mathf.Abs(maximum.y - minimum.y)));
            _collider.isTrigger = true;
            gameObject.SetActive(true);
        }

        internal void Release()
        {
            _rooms.Clear();
            CellBounds = default;
            gameObject.SetActive(false);
        }
    }
}
