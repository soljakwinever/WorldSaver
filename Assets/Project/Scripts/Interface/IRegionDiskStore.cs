using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Interface
{
    public interface IRegionDiskStore
    {
        Awaitable<RuntimeRegion> LoadAsync(Vector2Int regionPosition);
    }
}