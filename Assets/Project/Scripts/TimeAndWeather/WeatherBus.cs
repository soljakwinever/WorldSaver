using System;
using UnityEngine;

namespace Project.Scripts.TimeAndWeather
{
    public enum WeatherEffectEventType : byte
    {
        Started,
        Pulse,
        Stopped
    }

    public readonly struct WeatherRegionChangedEvent
    {
        public readonly Vector2Int Region;
        public readonly string PreviousWeatherId;
        public readonly string WeatherId;
        public readonly string PhaseId;
        public readonly long WorldTick;

        public WeatherRegionChangedEvent(
            Vector2Int region,
            string previousWeatherId,
            string weatherId,
            string phaseId,
            long worldTick)
        {
            Region = region;
            PreviousWeatherId = previousWeatherId ?? string.Empty;
            WeatherId = weatherId ?? string.Empty;
            PhaseId = phaseId ?? string.Empty;
            WorldTick = worldTick;
        }
    }

    public readonly struct WeatherEffectEvent
    {
        public readonly WeatherEffectEventType EventType;
        public readonly Vector2Int Region;
        public readonly Rect WorldBounds;
        public readonly string WeatherId;
        public readonly string PhaseId;
        public readonly WeatherEffectData Effect;
        public readonly float Intensity;
        public readonly long WorldTick;
        public readonly Vector2? Position;

        public WeatherEffectEvent(
            WeatherEffectEventType eventType,
            Vector2Int region,
            Rect worldBounds,
            string weatherId,
            string phaseId,
            WeatherEffectData effect,
            float intensity,
            long worldTick,
            Vector2? position = null)
        {
            EventType = eventType;
            Region = region;
            WorldBounds = worldBounds;
            WeatherId = weatherId ?? string.Empty;
            PhaseId = phaseId ?? string.Empty;
            Effect = effect;
            Intensity = Mathf.Clamp01(intensity);
            WorldTick = worldTick;
            Position = position;
        }
    }

    public sealed class WeatherBus
    {
        public event Action<WeatherRegionChangedEvent> RegionChanged;
        public event Action<WeatherEffectEvent> Effect;

        internal void RaiseRegionChanged(WeatherRegionChangedEvent change) =>
            RegionChanged?.Invoke(change);

        internal void RaiseEffect(WeatherEffectEvent effect) =>
            Effect?.Invoke(effect);
    }
}
