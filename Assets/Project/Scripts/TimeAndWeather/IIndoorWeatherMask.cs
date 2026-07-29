using UnityEngine;

namespace Project.Scripts.TimeAndWeather
{
    /// <summary>
    /// Supplies room-aware visibility without coupling the weather assembly to
    /// the game's room or player implementations.
    /// </summary>
    public interface IIndoorWeatherMask
    {
        bool IsViewerIndoors { get; }
        bool IsWorldPositionIndoors(Vector2 worldPosition);
    }
}
