using UnityEngine;

namespace Project.Scripts.Interface
{
    public interface IIndoorLocationService
    {
        bool IsWorldPositionIndoors(Vector2 worldPosition);
        bool TryFindNearestIndoorPosition(Vector2 origin, Vector2 areaCenter,
            float areaRadius, out Vector2 position);
    }
}
