using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "Plane", menuName = "World Generation/Plane")]
    public sealed class PlaneData : ScriptableObject
    {
        [SerializeField] private string persistentId = "surface";
        public WorldGenerationPresetData generationPreset;

        [Header("Lighting")]
        [Tooltip("When enabled, this plane uses the world's day/night color gradient.")]
        [SerializeField] private bool participatesInDayNightCycle = true;
        [ColorUsage(false, true)]
        [Tooltip("Fixed global ambient light used when the day/night cycle is disabled for this plane.")]
        [SerializeField] private Color ambientColor = Color.white;

        [Header("Audio")]
        [Tooltip("Default low-priority music for this plane.")]
        [SerializeField] private SongData defaultMusic;

        [Header("Weather")]
        [Tooltip("Weather definitions that may occur on this plane. Leave empty to allow all weather.")]
        [SerializeField] private WeatherData[] validWeather =
            Array.Empty<WeatherData>();

        public string PersistentId => persistentId?.Trim() ?? string.Empty;
        public bool ParticipatesInDayNightCycle =>
            participatesInDayNightCycle;
        public Color AmbientColor => ambientColor;
        public SongData DefaultMusic => defaultMusic;
        public WeatherData[] ValidWeather =>
            validWeather ?? Array.Empty<WeatherData>();

        public bool AllowsWeather(string weatherId)
        {
            WeatherData[] allowed = ValidWeather;
            if (allowed.Length == 0)
                return true;
            if (string.IsNullOrWhiteSpace(weatherId))
                return false;

            string normalizedId = weatherId.Trim();
            for (int i = 0; i < allowed.Length; i++)
            {
                WeatherData weather = allowed[i];
                if (weather != null && string.Equals(
                        weather.WeatherId,
                        normalizedId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void OnValidate()
        {
            persistentId = persistentId?.Trim();
            if (string.IsNullOrWhiteSpace(persistentId))
                Debug.LogError("Plane ID cannot be empty.", this);
            if (generationPreset == null)
                Debug.LogError("Plane requires a world-generation preset.", this);
        }
    }

    public sealed class PlaneSelection
    {
        public const string ActivePlaneIdKeyPrefix =
            "WorldSaver.ActivePlane.";

        public PlaneData Plane { get; }
        public string PlaneId => Plane.PersistentId;

        public static string GetActivePlaneIdKey()
        {
            string worldId = PlayerPrefs.GetString(
                "WorldSaver.ActiveWorld", "default");
            return ActivePlaneIdKeyPrefix + worldId;
        }

        public PlaneSelection(PlaneData plane)
        {
            Plane = plane != null
                ? plane
                : throw new System.ArgumentNullException(nameof(plane));
        }
    }
}
