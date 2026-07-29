using System.Collections.Generic;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts
{
    /// <summary>
    /// Groups rooms whose bounds are within a configured cell distance.
    /// Connections are transitive so a row of adjacent rooms forms one building.
    /// </summary>
    public static class BuildingRoomClusterer
    {
        public static void Build(
            IReadOnlyCollection<Room> rooms,
            int connectionDistance,
            List<BuildingRoomCluster> results)
        {
            results.Clear();
            if (rooms == null || rooms.Count == 0)
                return;

            connectionDistance = Mathf.Max(0, connectionDistance);
            List<Room> roomList = new(rooms.Count);
            foreach (Room room in rooms)
            {
                if (room != null && room.Area > 0)
                    roomList.Add(room);
            }
            roomList.Sort(CompareByMinimumX);

            int[] parents = new int[roomList.Count];
            for (int i = 0; i < parents.Length; i++)
                parents[i] = i;

            for (int i = 0; i < roomList.Count; i++)
            {
                for (int j = i + 1; j < roomList.Count; j++)
                {
                    if (roomList[j].Bounds.xMin -
                        roomList[i].Bounds.xMax >
                        connectionDistance)
                    {
                        break;
                    }

                    if (AreCloselyConnected(
                            roomList[i].Bounds,
                            roomList[j].Bounds,
                            connectionDistance))
                    {
                        Union(parents, i, j);
                    }
                }
            }

            Dictionary<int, BuildingRoomCluster> clusters = new();
            for (int i = 0; i < roomList.Count; i++)
            {
                int root = Find(parents, i);
                if (!clusters.TryGetValue(root, out BuildingRoomCluster cluster))
                {
                    cluster = new BuildingRoomCluster();
                    clusters.Add(root, cluster);
                    results.Add(cluster);
                }
                cluster.Add(roomList[i]);
            }
        }

        private static int CompareByMinimumX(Room first, Room second)
        {
            int comparison = first.Bounds.xMin.CompareTo(second.Bounds.xMin);
            return comparison != 0
                ? comparison
                : first.Id.CompareTo(second.Id);
        }

        public static bool AreCloselyConnected(
            BoundsInt first,
            BoundsInt second,
            int connectionDistance)
        {
            connectionDistance = Mathf.Max(0, connectionDistance);
            int horizontalGap = Mathf.Max(
                0,
                Mathf.Max(
                    second.xMin - first.xMax,
                    first.xMin - second.xMax));
            int verticalGap = Mathf.Max(
                0,
                Mathf.Max(
                    second.yMin - first.yMax,
                    first.yMin - second.yMax));
            return horizontalGap <= connectionDistance &&
                   verticalGap <= connectionDistance;
        }

        private static int Find(int[] parents, int index)
        {
            while (parents[index] != index)
            {
                parents[index] = parents[parents[index]];
                index = parents[index];
            }
            return index;
        }

        private static void Union(int[] parents, int first, int second)
        {
            int firstRoot = Find(parents, first);
            int secondRoot = Find(parents, second);
            if (firstRoot != secondRoot)
                parents[secondRoot] = firstRoot;
        }
    }

    public sealed class BuildingRoomCluster
    {
        private readonly List<Room> _rooms = new();

        public BoundsInt Bounds { get; private set; }
        public IReadOnlyList<Room> Rooms => _rooms;

        internal void Add(Room room)
        {
            if (_rooms.Count == 0)
            {
                Bounds = room.Bounds;
            }
            else
            {
                Vector3Int minimum = Vector3Int.Min(Bounds.min, room.Bounds.min);
                Vector3Int maximum = Vector3Int.Max(Bounds.max, room.Bounds.max);
                Bounds = new BoundsInt(minimum, maximum - minimum);
            }
            _rooms.Add(room);
        }
    }
}
