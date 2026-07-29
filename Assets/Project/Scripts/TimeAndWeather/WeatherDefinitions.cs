using System;

namespace Project.Scripts.TimeAndWeather
{
    [Flags]
    public enum WeatherSeasonMask : byte
    {
        None = 0,
        Spring = 1 << 0,
        Summer = 1 << 1,
        Autumn = 1 << 2,
        Winter = 1 << 3,
        All = Spring | Summer | Autumn | Winter
    }

    public enum WeatherEffectKind : byte
    {
        Custom,
        Rain,
        Snow,
        Ash,
        FlowerPetals,
        Lightning,
        Tornado,
        Temperature,
        PuddleAccumulation,
        SnowAccumulation
    }
}
