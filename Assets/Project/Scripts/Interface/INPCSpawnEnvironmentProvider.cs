namespace Project.Scripts.Interface
{
    /// <summary>
    /// Extension point for event and weather systems. Empty rule values bypass it.
    /// </summary>
    public interface INPCSpawnEnvironmentProvider
    {
        bool IsEventActive(string eventId);
        bool IsWeatherActive(string weatherId);
        float GetAmbientTemperature();
    }

    public sealed class NullNPCSpawnEnvironmentProvider : INPCSpawnEnvironmentProvider
    {
        public bool IsEventActive(string eventId) => false;
        public bool IsWeatherActive(string weatherId) => false;
        public float GetAmbientTemperature() => 0f;
    }
}
