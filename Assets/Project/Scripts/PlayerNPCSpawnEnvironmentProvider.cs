using System;
using Project.Scripts.Interface;

namespace Project.Scripts
{
    /// <summary>
    /// Evaluates NPC spawn conditions against the environment at the player's
    /// current tracked position.
    /// </summary>
    public sealed class PlayerNPCSpawnEnvironmentProvider :
        INPCSpawnEnvironmentProvider
    {
        private readonly Chunkloader _chunkloader;
        private readonly IEventService _events;

        public PlayerNPCSpawnEnvironmentProvider(
            Chunkloader chunkloader,
            IEventService events)
        {
            _chunkloader = chunkloader;
            _events = events;
        }

        public bool IsEventActive(string eventId)
        {
            return _events != null && _events.IsActive(eventId);
        }

        public bool IsWeatherActive(string weatherId)
        {
            if (_chunkloader == null ||
                _chunkloader.track == null ||
                string.IsNullOrWhiteSpace(weatherId))
            {
                return false;
            }

            return string.Equals(
                _chunkloader.CurrentWeather.WeatherId,
                weatherId.Trim(),
                StringComparison.Ordinal);
        }

        public float GetAmbientTemperature()
        {
            return _chunkloader == null || _chunkloader.track == null
                ? 0f
                : _chunkloader.CurrentAmbientTemperature;
        }
    }
}
