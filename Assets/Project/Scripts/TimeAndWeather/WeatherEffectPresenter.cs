using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Zenject;
using Object = UnityEngine.Object;

namespace Project.Scripts.TimeAndWeather
{
    /// <summary>
    /// Presents weather prefabs and maintains camera-relative full-screen
    /// particle systems. Gameplay systems can also consume WeatherBus directly.
    /// </summary>
    public sealed class WeatherEffectPresenter :
        IInitializable,
        ITickable,
        IDisposable
    {
        private readonly WeatherBus _bus;
        private readonly IIndoorWeatherMask _indoorMask;
        private readonly IRegionalWeatherService _weather;
        private readonly Dictionary<EffectKey, GameObject> _phaseInstances = new();
        private readonly Dictionary<EffectKey, FullScreenEffect> _fullScreen = new();
        private readonly List<RetiringParticleEffect> _retiring = new();
        private readonly List<EffectKey> _fullScreenRemovals = new();
        private Camera _camera;
        private CancellationTokenSource _refreshCancellation;
        private float _nextRetiringCheckTime;
        private Vector2Int _cameraRegion;
        private bool _cameraHasWeatherOverride;
        private bool _viewerIndoors;

        private sealed class FullScreenEffect
        {
            public WeatherEffectData Effect;
            public GameObject Instance;
            public ParticleEmission[] Emissions;
            public float TargetIntensity;
            public float CurrentIntensity;
            public float LastCameraHeight = -1f;
        }

        private sealed class RetiringParticleEffect
        {
            public GameObject Instance;
            public ParticleSystem[] Particles;
            public float DestroyAt;
        }

        private readonly struct ParticleEmission
        {
            public readonly ParticleSystem ParticleSystem;
            public readonly ParticleSystem.MinMaxCurve RateOverTime;
            public readonly ParticleSystem.MinMaxCurve RateOverDistance;

            public ParticleEmission(ParticleSystem particleSystem)
            {
                ParticleSystem = particleSystem;
                ParticleSystem.EmissionModule emission = particleSystem.emission;
                RateOverTime = emission.rateOverTime;
                RateOverDistance = emission.rateOverDistance;
            }
        }

        private readonly struct EffectKey : IEquatable<EffectKey>
        {
            public readonly Vector2Int Region;
            public readonly WeatherEffectData Effect;

            public EffectKey(Vector2Int region, WeatherEffectData effect)
            {
                Region = region;
                Effect = effect;
            }

            public bool Equals(EffectKey other) =>
                Region == other.Region && Effect == other.Effect;

            public override bool Equals(object obj) =>
                obj is EffectKey other && Equals(other);

            public override int GetHashCode() =>
                (Region.GetHashCode() * 397) ^ (Effect == null
                    ? 0
                    : Effect.GetHashCode());
        }

        public WeatherEffectPresenter(
            WeatherBus bus,
            IIndoorWeatherMask indoorMask,
            IRegionalWeatherService weather)
        {
            _bus = bus;
            _indoorMask = indoorMask;
            _weather = weather;
        }

        public void Initialize()
        {
            _bus.Effect += OnEffect;
            _refreshCancellation = new CancellationTokenSource();
            RefreshEnvironmentAsync(_refreshCancellation.Token).Forget();
        }

        public void Tick()
        {
            if (_retiring.Count > 0 &&
                Time.unscaledTime >= _nextRetiringCheckTime)
            {
                _nextRetiringCheckTime = Time.unscaledTime + 0.1f;
                ProcessRetiringParticleEffects();
            }

            Camera camera = _camera;
            if (camera == null)
                return;

            foreach (KeyValuePair<EffectKey, FullScreenEffect> pair in _fullScreen)
            {
                bool inCameraRegion = pair.Key.Region == _cameraRegion;
                bool shouldBeVisible = ShouldPresentFullScreenEffect(
                    inCameraRegion,
                    _viewerIndoors);
                FullScreenEffect fullScreen = pair.Value;

                if (shouldBeVisible && fullScreen.Instance == null)
                    CreateFullScreenInstance(fullScreen, camera);
                else if (shouldBeVisible &&
                         !fullScreen.Instance.activeSelf)
                    ResumeFullScreenInstance(fullScreen);

                if (fullScreen.Instance == null ||
                    !fullScreen.Instance.activeSelf)
                    continue;

                if (_viewerIndoors)
                {
                    // Indoor transitions clear existing precipitation
                    // immediately instead of waiting for particles to expire.
                    SuspendFullScreenInstance(fullScreen);
                    continue;
                }

                UpdateFullScreenTransform(fullScreen, camera);
                float desired = shouldBeVisible
                    ? fullScreen.TargetIntensity
                    : 0f;
                float blendSeconds = shouldBeVisible
                    ? fullScreen.Effect.FullScreenBlendInSeconds
                    : fullScreen.Effect.FullScreenBlendOutSeconds;
                float previousIntensity = fullScreen.CurrentIntensity;
                fullScreen.CurrentIntensity = Mathf.MoveTowards(
                    fullScreen.CurrentIntensity,
                    desired,
                    Time.deltaTime / blendSeconds);
                if (!Mathf.Approximately(
                        previousIntensity,
                        fullScreen.CurrentIntensity))
                {
                    ApplyEmission(fullScreen);
                }

                if (!shouldBeVisible &&
                    fullScreen.CurrentIntensity <= Mathf.Epsilon)
                {
                    SuspendFullScreenInstance(fullScreen);
                }
            }
        }

        /// <summary>
        /// Spreads weather sampling, effect synchronization, and world-effect
        /// masking across separate frames. Awaitable.NextFrameAsync resumes on
        /// Unity's main thread, which is required by every operation here.
        /// </summary>
        private async Awaitable RefreshEnvironmentAsync(
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_camera == null)
                    _camera = Camera.main;

                Camera camera = _camera;
                if (camera == null)
                {
                    _cameraHasWeatherOverride = false;
                    _viewerIndoors = false;
                    DisableAllFullScreenEffects();
                    await Awaitable.NextFrameAsync(cancellationToken);
                    continue;
                }

                Vector2 viewerPosition =
                    _indoorMask != null &&
                    _indoorMask.TryGetViewerWorldPosition(
                        out Vector2 playerPosition)
                        ? playerPosition
                        : camera.transform.position;
                Vector2Int cameraRegion =
                    WeatherRegionUtility.WorldToRegion(viewerPosition);
                bool viewerIndoors =
                    _indoorMask?.IsViewerIndoors ?? false;
                WeatherSample cameraSample = _weather.Sample(viewerPosition);

                await Awaitable.NextFrameAsync(cancellationToken);

                _cameraRegion = cameraRegion;
                _viewerIndoors = viewerIndoors;
                _cameraHasWeatherOverride =
                    cameraSample.HasWeatherOverride;
                SyncCameraFullScreenEffects(cameraRegion, cameraSample);

                await Awaitable.NextFrameAsync(cancellationToken);

                UpdateWorldEffectMasking();

                await Awaitable.NextFrameAsync(cancellationToken);
            }
        }

        private void SyncCameraFullScreenEffects(
            Vector2Int cameraRegion,
            WeatherSample sample)
        {
            _fullScreenRemovals.Clear();
            foreach (KeyValuePair<EffectKey, FullScreenEffect> pair in
                     _fullScreen)
            {
                if (pair.Key.Region == cameraRegion &&
                    !ContainsFullScreenEffect(
                        sample.ActiveEffects,
                        pair.Key.Effect))
                {
                    _fullScreenRemovals.Add(pair.Key);
                }
            }

            foreach (EffectKey key in _fullScreenRemovals)
                StopFullScreenEffect(key);

            foreach (WeatherEffectSample active in sample.ActiveEffects)
            {
                WeatherEffectData effect = active.Effect;
                if (effect == null || !effect.IsFullScreenParticleEffect)
                    continue;

                EffectKey key = new(cameraRegion, effect);
                if (_fullScreen.TryGetValue(
                        key,
                        out FullScreenEffect existing))
                {
                    existing.TargetIntensity = active.Intensity;
                    if (sample.HasWeatherOverride &&
                        !Mathf.Approximately(
                            existing.CurrentIntensity,
                            active.Intensity))
                    {
                        // Spatial overrides describe the intensity at the
                        // camera's exact position. Apply that value directly
                        // so moving from the outer edge to the inner radius
                        // maps 0..1 to the authored particle rate without a
                        // regional weather transition lag.
                        existing.CurrentIntensity = active.Intensity;
                        ApplyEmission(existing);
                    }
                    continue;
                }

                _fullScreen.Add(key, new FullScreenEffect
                {
                    Effect = effect,
                    TargetIntensity = active.Intensity,
                    CurrentIntensity = sample.HasWeatherOverride
                        ? active.Intensity
                        : 0f
                });
            }
        }

        private static bool ContainsFullScreenEffect(
            WeatherEffectSample[] activeEffects,
            WeatherEffectData effect)
        {
            foreach (WeatherEffectSample active in
                     activeEffects ?? Array.Empty<WeatherEffectSample>())
            {
                if (active.Effect == effect &&
                    active.Effect != null &&
                    active.Effect.IsFullScreenParticleEffect)
                {
                    return true;
                }
            }

            return false;
        }

        public void Dispose()
        {
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            _refreshCancellation = null;
            _bus.Effect -= OnEffect;
            foreach (GameObject instance in _phaseInstances.Values)
            {
                if (instance != null)
                    Object.Destroy(instance);
            }
            _phaseInstances.Clear();

            foreach (FullScreenEffect effect in _fullScreen.Values)
                DestroyFullScreenInstance(effect);
            _fullScreen.Clear();

            foreach (RetiringParticleEffect retiring in _retiring)
            {
                if (retiring.Instance != null)
                    Object.Destroy(retiring.Instance);
            }
            _retiring.Clear();
        }

        private void OnEffect(WeatherEffectEvent message)
        {
            WeatherEffectData effect = message.Effect;
            if (effect == null)
                return;

            EffectKey key = new(message.Region, effect);
            if (effect.IsFullScreenParticleEffect)
            {
                // A position-specific override is authoritative at the
                // camera. Regional lifecycle events are emitted independently
                // and may arrive after this presenter's Tick; accepting them
                // here would replace the override intensity (often with the
                // regional phase's zero-at-start value).
                if (!ShouldApplyRegionalFullScreenEvent(
                        _cameraHasWeatherOverride,
                        _cameraRegion,
                        message.Region))
                {
                    return;
                }

                HandleFullScreenEvent(key, message);
                return;
            }

            if (effect.EffectPrefab == null)
                return;

            switch (message.EventType)
            {
                case WeatherEffectEventType.Started
                    when effect.SpawnOnPhaseStart:
                    StopPhaseInstance(key);
                    _phaseInstances[key] = SpawnWorldEffect(
                        message,
                        destroyAfterLifetime: false);
                    break;

                case WeatherEffectEventType.Pulse
                    when effect.SpawnOnPulse:
                    SpawnWorldEffect(message, destroyAfterLifetime: true);
                    break;

                case WeatherEffectEventType.Stopped:
                    StopPhaseInstance(key);
                    break;
            }
        }

        private void HandleFullScreenEvent(
            EffectKey key,
            WeatherEffectEvent message)
        {
            switch (message.EventType)
            {
                case WeatherEffectEventType.Started:
                    StopFullScreenEffect(key);
                    _fullScreen[key] = new FullScreenEffect
                    {
                        Effect = message.Effect,
                        TargetIntensity = message.Intensity,
                        CurrentIntensity = 0f
                    };
                    break;

                case WeatherEffectEventType.Pulse:
                    if (_fullScreen.TryGetValue(
                            key,
                            out FullScreenEffect active))
                    {
                        active.TargetIntensity = message.Intensity;
                    }
                    break;

                case WeatherEffectEventType.Stopped:
                    StopFullScreenEffect(key);
                    break;
            }
        }

        private GameObject SpawnWorldEffect(
            WeatherEffectEvent message,
            bool destroyAfterLifetime)
        {
            Vector2 position = message.Position ??
                               message.WorldBounds.center;
            if (_indoorMask?.IsWorldPositionIndoors(position) == true)
                return null;

            GameObject instance = Object.Instantiate(
                message.Effect.EffectPrefab,
                new Vector3(position.x, position.y, 0f),
                Quaternion.identity);

            if (destroyAfterLifetime &&
                message.Effect.PrefabLifetimeSeconds > 0f)
            {
                Object.Destroy(
                    instance,
                    message.Effect.PrefabLifetimeSeconds);
            }

            return instance;
        }

        private void UpdateWorldEffectMasking()
        {
            if (_indoorMask == null)
                return;

            foreach (GameObject instance in _phaseInstances.Values)
            {
                if (instance == null)
                    continue;

                bool shouldBeVisible =
                    !_indoorMask.IsWorldPositionIndoors(
                        instance.transform.position);
                if (instance.activeSelf != shouldBeVisible)
                    instance.SetActive(shouldBeVisible);
            }
        }

        public static bool ShouldPresentFullScreenEffect(
            bool isCameraRegion,
            bool isViewerIndoors) =>
            isCameraRegion && !isViewerIndoors;

        public static bool ShouldApplyRegionalFullScreenEvent(
            bool cameraHasWeatherOverride,
            Vector2Int cameraRegion,
            Vector2Int eventRegion) =>
            !cameraHasWeatherOverride || cameraRegion != eventRegion;

        private static void CreateFullScreenInstance(
            FullScreenEffect fullScreen,
            Camera camera)
        {
            if (fullScreen.Effect.EffectPrefab == null)
                return;

            GameObject instance = Object.Instantiate(
                fullScreen.Effect.EffectPrefab,
                camera.transform);
            instance.name =
                $"{fullScreen.Effect.EffectId} (Full Screen Weather)";
            fullScreen.Instance = instance;

            ParticleSystem[] particles =
                instance.GetComponentsInChildren<ParticleSystem>(true);
            fullScreen.Emissions = new ParticleEmission[particles.Length];
            for (int i = 0; i < particles.Length; i++)
            {
                fullScreen.Emissions[i] = new ParticleEmission(particles[i]);
                particles[i].Stop(
                    withChildren: true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
                ParticleSystem.MainModule main = particles[i].main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;

                ParticleSystem.EmissionModule emission = particles[i].emission;
                emission.rateOverTimeMultiplier = 0f;
                emission.rateOverDistanceMultiplier = 0f;
                particles[i].Play(true);
            }

            ApplyEmission(fullScreen);
            UpdateFullScreenTransform(fullScreen, camera);
        }
        
        private static void UpdateFullScreenTransform(
            FullScreenEffect fullScreen,
            Camera camera)
        {
            float cameraHeight = camera.orthographic
                ? camera.orthographicSize * 2f
                : 2f * fullScreen.Effect.FullScreenCameraDistance *
                  Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            if (Mathf.Approximately(
                    fullScreen.LastCameraHeight,
                    cameraHeight))
            {
                return;
            }

            fullScreen.LastCameraHeight = cameraHeight;
            Transform transform = fullScreen.Instance.transform;
            transform.SetParent(camera.transform, worldPositionStays: false);
            transform.localPosition =
                Vector3.forward * fullScreen.Effect.FullScreenCameraDistance;
            transform.localRotation = Quaternion.identity;
            float scale = cameraHeight /
                          fullScreen.Effect.FullScreenReferenceHeight;
            transform.localScale = Vector3.one * scale;
        }

        private static void ApplyEmission(FullScreenEffect fullScreen)
        {
            if (fullScreen.Emissions == null)
                return;

            float intensity = Mathf.Clamp01(fullScreen.CurrentIntensity);
            foreach (ParticleEmission cached in fullScreen.Emissions)
            {
                if (cached.ParticleSystem == null)
                    continue;

                ParticleSystem.EmissionModule emission =
                    cached.ParticleSystem.emission;
                emission.rateOverTime = ScaleEmissionCurve(
                    cached.RateOverTime,
                    intensity);
                emission.rateOverDistance = ScaleEmissionCurve(
                    cached.RateOverDistance,
                    intensity);
            }
        }

        public static ParticleSystem.MinMaxCurve ScaleEmissionCurve(
            ParticleSystem.MinMaxCurve authored,
            float intensity)
        {
            float scale = Mathf.Clamp01(intensity);
            return authored.mode switch
            {
                ParticleSystemCurveMode.Constant =>
                    new ParticleSystem.MinMaxCurve(
                        authored.constant * scale),
                ParticleSystemCurveMode.TwoConstants =>
                    new ParticleSystem.MinMaxCurve(
                        authored.constantMin * scale,
                        authored.constantMax * scale),
                ParticleSystemCurveMode.Curve =>
                    new ParticleSystem.MinMaxCurve(
                        authored.curveMultiplier * scale,
                        authored.curve),
                ParticleSystemCurveMode.TwoCurves =>
                    new ParticleSystem.MinMaxCurve(
                        authored.curveMultiplier * scale,
                        authored.curveMin,
                        authored.curveMax),
                _ => authored
            };
        }

        public static float ScaleEmissionRate(
            float authoredRate,
            float intensity) =>
            authoredRate * Mathf.Clamp01(intensity);

        private static void SuspendFullScreenInstance(
            FullScreenEffect fullScreen)
        {
            if (fullScreen.Instance == null)
                return;

            if (fullScreen.Emissions != null)
            {
                foreach (ParticleEmission cached in fullScreen.Emissions)
                {
                    if (cached.ParticleSystem == null)
                        continue;

                    cached.ParticleSystem.Stop(
                        withChildren: true,
                        ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }

            fullScreen.CurrentIntensity = 0f;
            fullScreen.LastCameraHeight = -1f;
            fullScreen.Instance.SetActive(false);
        }

        private static void ResumeFullScreenInstance(
            FullScreenEffect fullScreen)
        {
            fullScreen.CurrentIntensity = 0f;
            fullScreen.LastCameraHeight = -1f;
            fullScreen.Instance.SetActive(true);
            ApplyEmission(fullScreen);

            if (fullScreen.Emissions == null)
                return;

            foreach (ParticleEmission cached in fullScreen.Emissions)
            {
                if (cached.ParticleSystem == null)
                    continue;

                cached.ParticleSystem.Stop(
                    withChildren: true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
                cached.ParticleSystem.Play(withChildren: true);
            }
        }

        private void StopPhaseInstance(EffectKey key)
        {
            if (!_phaseInstances.Remove(key, out GameObject instance) ||
                instance == null)
            {
                return;
            }

            RetireOrDestroy(instance, key.Effect);
        }

        private void StopFullScreenEffect(EffectKey key)
        {
            if (!_fullScreen.Remove(key, out FullScreenEffect fullScreen))
                return;

            RetireFullScreenInstance(fullScreen);
        }

        private static void DestroyFullScreenInstance(
            FullScreenEffect fullScreen)
        {
            if (fullScreen.Instance != null)
                Object.Destroy(fullScreen.Instance);
            fullScreen.Instance = null;
            fullScreen.Emissions = null;
            fullScreen.CurrentIntensity = 0f;
        }

        private void DisableAllFullScreenEffects()
        {
            foreach (FullScreenEffect effect in _fullScreen.Values)
                RetireFullScreenInstance(effect);
        }

        private void RetireFullScreenInstance(FullScreenEffect fullScreen)
        {
            GameObject instance = fullScreen.Instance;
            fullScreen.Instance = null;
            fullScreen.Emissions = null;
            fullScreen.CurrentIntensity = 0f;
            RetireOrDestroy(instance, fullScreen.Effect);
        }

        private void RetireOrDestroy(
            GameObject instance,
            WeatherEffectData effect)
        {
            if (instance == null)
                return;

            ParticleSystem[] particles =
                instance.GetComponentsInChildren<ParticleSystem>(true);
            if (effect == null ||
                !effect.FinishParticlesOnStop ||
                particles.Length == 0)
            {
                Object.Destroy(instance);
                return;
            }

            foreach (ParticleSystem particle in particles)
            {
                if (particle != null)
                {
                    particle.Stop(
                        withChildren: true,
                        ParticleSystemStopBehavior.StopEmitting);
                }
            }

            _retiring.Add(new RetiringParticleEffect
            {
                Instance = instance,
                Particles = particles,
                DestroyAt = Time.time + effect.ParticleStopTimeoutSeconds
            });
        }

        private void ProcessRetiringParticleEffects()
        {
            for (int i = _retiring.Count - 1; i >= 0; i--)
            {
                RetiringParticleEffect retiring = _retiring[i];
                if (retiring.Instance == null)
                {
                    _retiring.RemoveAt(i);
                    continue;
                }

                bool alive = false;
                foreach (ParticleSystem particle in retiring.Particles)
                {
                    if (particle != null && particle.IsAlive(withChildren: true))
                    {
                        alive = true;
                        break;
                    }
                }

                if (alive && Time.time < retiring.DestroyAt)
                    continue;

                Object.Destroy(retiring.Instance);
                _retiring.RemoveAt(i);
            }
        }
    }
}
