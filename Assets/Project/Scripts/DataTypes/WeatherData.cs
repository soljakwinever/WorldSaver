using UnityEngine;

namespace Project.Scripts.DataTypes
{
    /// <summary>
    /// Stable weather identity shared with systems that cannot depend on the
    /// weather simulation assembly.
    /// </summary>
    public abstract class WeatherData : ScriptableObject
    {
        [SerializeField] private string weatherId = "weather";

        public string WeatherId => string.IsNullOrWhiteSpace(weatherId)
            ? name
            : weatherId.Trim();
    }
}
