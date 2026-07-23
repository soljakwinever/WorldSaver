using Project.Scripts.DataTypes.SaveData;
using UnityEngine;

namespace Project.Scripts.Interface
{
    public interface IRegionDiskStore
    {
        Awaitable<RegionSaveData> LoadAsync(Vector2Int regionPosition);
        Awaitable SaveAsync(RegionSaveData snapshot);
    }
}
