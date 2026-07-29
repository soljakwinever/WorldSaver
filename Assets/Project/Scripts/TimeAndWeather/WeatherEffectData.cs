using UnityEngine;

namespace Project.Scripts.TimeAndWeather
{
    public abstract class WeatherEffectData : ScriptableObject
    {
        [SerializeField] private string effectId = "effect";
        [SerializeField] private WeatherEffectKind kind;
        [SerializeField] private AnimationCurve intensity =
            AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField, Min(1)] private int pulseIntervalTicks = 1;

        public string EffectId => string.IsNullOrWhiteSpace(effectId)
            ? name
            : effectId.Trim();
        public WeatherEffectKind Kind => kind;
        public int PulseIntervalTicks => Mathf.Max(1, pulseIntervalTicks);

        public virtual float TemperatureOffset => 0f;
        public virtual float PuddleAccumulationPerTick => 0f;
        public virtual float SnowAccumulationPerTick => 0f;
        public virtual GameObject EffectPrefab => null;
        public virtual float SpawnChancePerPulse => 1f;
        public virtual bool SpawnOnPhaseStart => false;
        public virtual bool SpawnOnPulse => false;
        public virtual float PrefabLifetimeSeconds => 0f;
        public virtual bool IsFullScreenParticleEffect => false;
        public virtual float FullScreenReferenceHeight => 10f;
        public virtual float FullScreenCameraDistance => 5f;
        public virtual float FullScreenBlendInSeconds => 2f;
        public virtual float FullScreenBlendOutSeconds => 1f;
        public virtual bool FinishParticlesOnStop => true;
        public virtual float ParticleStopTimeoutSeconds => 30f;

        public float EvaluateIntensity(float phaseProgress)
        {
            return Mathf.Clamp01(
                intensity == null
                    ? phaseProgress
                    : intensity.Evaluate(Mathf.Clamp01(phaseProgress)));
        }
    }
}
