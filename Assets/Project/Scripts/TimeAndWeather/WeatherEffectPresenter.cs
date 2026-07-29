using System;
using System.Collections.Generic;
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
        private readonly Dictionary<EffectKey, GameObject> _phaseInstances = new();
        private readonly Dictionary<EffectKey, FullScreenEffect> _fullScreen = new();
        private readonly List<RetiringParticleEffect> _retiring = new();
        private Camera _camera;
        private float _nextRetiringCheckTime;

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
            public readonly float RateOverTimeMultiplier;
            public readonly float RateOverDistanceMultiplier;

            public ParticleEmission(ParticleSystem particleSystem)
            {
                ParticleSystem = particleSystem;
                ParticleSystem.EmissionModule emission = particleSystem.emission;
                RateOverTimeMultiplier = emission.rateOverTimeMultiplier;
                RateOverDistanceMultiplier = emission.rateOverDistanceMultiplier;
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

        public WeatherEffectPresenter(WeatherBus bus)
        {
            _bus = bus;
        }

        public void Initialize()
        {
            _bus.Effect += OnEffect;
        }

        public void Tick()
        {
            if (_retiring.Count > 0 &&
                Time.unscaledTime >= _nextRetiringCheckTime)
            {
                _nextRetiringCheckTime = Time.unscaledTime + 0.1f;
                ProcessRetiringParticleEffects();
            }

            if (_camera == null)
                _camera = Camera.main;
            Camera camera = _camera;
            if (camera == null)
            {
                DisableAllFullScreenEffects();
                return;
            }

            Vector2 cameraPosition = camera.transform.position;
            Vector2Int cameraRegion =
                WeatherRegionUtility.WorldToRegion(cameraPosition);

            foreach (KeyValuePair<EffectKey, FullScreenEffect> pair in _fullScreen)
            {
                bool shouldBeVisible = pair.Key.Region == cameraRegion;
                FullScreenEffect fullScreen = pair.Value;

                if (shouldBeVisible && fullScreen.Instance == null)
                    CreateFullScreenInstance(fullScreen, camera);
                else if (shouldBeVisible &&
                         !fullScreen.Instance.activeSelf)
                    ResumeFullScreenInstance(fullScreen);

                if (fullScreen.Instance == null ||
                    !fullScreen.Instance.activeSelf)
                    continue;

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

        public void Dispose()
        {
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
                emission.rateOverTimeMultiplier =
                    cached.RateOverTimeMultiplier * intensity;
                emission.rateOverDistanceMultiplier =
                    cached.RateOverDistanceMultiplier * intensity;
            }
        }

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
