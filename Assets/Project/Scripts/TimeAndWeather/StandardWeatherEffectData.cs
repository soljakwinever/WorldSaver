using UnityEngine;

namespace Project.Scripts.TimeAndWeather
{
    [CreateAssetMenu(
        fileName = "Weather Effect",
        menuName = "World Saver/Weather/Standard Effect")]
    public sealed class StandardWeatherEffectData : WeatherEffectData
    {
        [Header("Optional presentation or hazard prefab")]
        [SerializeField] private GameObject effectPrefab;
        [SerializeField] private bool spawnOnPhaseStart;
        [SerializeField] private bool spawnOnPulse = true;
        [SerializeField, Range(0f, 1f)] private float spawnChancePerPulse = 1f;
        [SerializeField, Min(0f)] private float prefabLifetimeSeconds = 5f;

        [Header("Full-screen particles")]
        [Tooltip("Parents one persistent prefab instance to the main camera while this effect is active in the camera's region.")]
        [SerializeField] private bool fullScreenParticleEffect;
        [Tooltip("The prefab height, in local units, that should fill the camera vertically at scale 1.")]
        [SerializeField, Min(0.01f)] private float fullScreenReferenceHeight = 10f;
        [SerializeField, Min(0.01f)] private float fullScreenCameraDistance = 5f;
        [Tooltip("Seconds for emission to blend from clear weather to full intensity.")]
        [SerializeField, Min(0.01f)] private float fullScreenBlendInSeconds = 2f;
        [Tooltip("Seconds for emission to blend out when leaving the weather region.")]
        [SerializeField, Min(0.01f)] private float fullScreenBlendOutSeconds = 1f;

        [Header("Particle cleanup")]
        [Tooltip("Stops emission and waits for existing particles to die naturally before destroying the prefab.")]
        [SerializeField] private bool finishParticlesOnStop = true;
        [Tooltip("Safety limit for looping particles or sub-emitters that never finish.")]
        [SerializeField, Min(0.1f)] private float particleStopTimeoutSeconds = 30f;

        [Header("Environmental influence")]
        [SerializeField, Range(-2f, 2f)] private float temperatureOffset;
        [SerializeField, Min(0f)] private float puddleAccumulationPerTick;
        [SerializeField, Min(0f)] private float snowAccumulationPerTick;

        public override float TemperatureOffset => temperatureOffset;
        public override float PuddleAccumulationPerTick =>
            puddleAccumulationPerTick;
        public override float SnowAccumulationPerTick =>
            snowAccumulationPerTick;
        public override GameObject EffectPrefab => effectPrefab;
        public override float SpawnChancePerPulse =>
            Mathf.Clamp01(spawnChancePerPulse);
        public override bool SpawnOnPhaseStart => spawnOnPhaseStart;
        public override bool SpawnOnPulse => spawnOnPulse;
        public override float PrefabLifetimeSeconds => prefabLifetimeSeconds;
        public override bool IsFullScreenParticleEffect =>
            fullScreenParticleEffect;
        public override float FullScreenReferenceHeight =>
            Mathf.Max(0.01f, fullScreenReferenceHeight);
        public override float FullScreenCameraDistance =>
            Mathf.Max(0.01f, fullScreenCameraDistance);
        public override float FullScreenBlendInSeconds =>
            Mathf.Max(0.01f, fullScreenBlendInSeconds);
        public override float FullScreenBlendOutSeconds =>
            Mathf.Max(0.01f, fullScreenBlendOutSeconds);
        public override bool FinishParticlesOnStop => finishParticlesOnStop;
        public override float ParticleStopTimeoutSeconds =>
            Mathf.Max(0.1f, particleStopTimeoutSeconds);
    }
}
