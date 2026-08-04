using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Interface
{
    public interface IPlantTileContext
    {
        bool TryGetPlantingTile(Vector3Int worldCell, out TileData tile);
        float GetPlantWater(Vector3Int worldCell);
        float AddPlantWater(Vector3Int worldCell, float amount, float maximum);
        float ConsumePlantWater(Vector3Int worldCell, float amount);
        void SetPlantWaterColor(Vector3Int worldCell, Color color);
        bool DoesWeatherWaterPlants(Vector3Int worldCell);
    }
}
