using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using Project.Scripts.TimeAndWeather;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    public sealed class DangerService : IDangerService, IInitializable,
        ITickable, IDisposable
    {
        private const string ParameterName = "Danger";
        private readonly IAudioService _audio;
        private readonly PlayerDataController _player;
        private readonly IRegionalWeatherService _weather;
        private readonly DangerSettings _settings;
        private readonly Dictionary<string, float> _eventContributions =
            new(StringComparer.Ordinal);
        private float _elapsed;

        public DangerService(IAudioService audio, PlayerDataController player,
            IRegionalWeatherService weather, DangerSettings settings)
        {
            _audio = audio;
            _player = player;
            _weather = weather;
            _settings = settings;
        }

        public void Initialize() => Refresh();

        public void Tick()
        {
            _elapsed += Mathf.Max(0f, Time.unscaledDeltaTime);
            float interval = Mathf.Max(0.05f, _settings.updateInterval);
            if (_elapsed < interval)
                return;
            _elapsed %= interval;
            Refresh();
        }

        public void Dispose()
        {
            _eventContributions.Clear();
            _audio.SetEventParameter(ParameterName, 0f);
        }

        public void SetEventContribution(string ownerKey, float value)
        {
            ownerKey = ownerKey?.Trim();
            if (string.IsNullOrEmpty(ownerKey))
                return;
            _eventContributions[ownerKey] = Mathf.Clamp01(value);
        }

        public void ClearEventContribution(string ownerKey)
        {
            ownerKey = ownerKey?.Trim();
            if (!string.IsNullOrEmpty(ownerKey))
                _eventContributions.Remove(ownerKey);
        }

        private void Refresh()
        {
            if (_player == null)
            {
                _audio.SetEventParameter(ParameterName, 0f);
                return;
            }

            float danger = CalculateEnemyDanger(
                _player.transform.position,
                Mathf.Max(0f, _settings.detectionRadius),
                UnityEngine.Object.FindObjectsByType<EnemyRuntime>(
                    FindObjectsSortMode.None));
            danger += CalculateWeatherDanger(
                _weather?.Sample(_player.transform.position) ?? default);
            foreach (float contribution in _eventContributions.Values)
                danger += contribution;
            _audio.SetEventParameter(ParameterName, Mathf.Clamp01(danger));
        }

        public static float CalculateEnemyDanger(Vector2 playerPosition,
            float radius, IReadOnlyList<EnemyRuntime> enemies)
        {
            float total = 0f;
            float radiusSquared = radius * radius;
            if (enemies == null)
                return total;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyRuntime enemy = enemies[i];
                if (enemy == null || enemy.Data == null)
                    continue;
                TransientHealth health = enemy.GetComponent<TransientHealth>();
                if (health != null && health.Health <= 0)
                    continue;
                if (((Vector2)enemy.transform.position - playerPosition)
                    .sqrMagnitude > radiusSquared)
                    continue;
                total += Mathf.Clamp01(enemy.Data.dangerLevel) *
                         Mathf.Clamp01(enemy.Data.dangerWeight);
            }
            return Mathf.Clamp01(total);
        }

        public static float CalculateWeatherDanger(WeatherSample sample)
        {
            float total = 0f;
            foreach (WeatherEffectSample active in
                     sample.ActiveEffects ?? Array.Empty<WeatherEffectSample>())
                if (active.Effect != null)
                    total += active.Effect.DangerContribution * active.Intensity;
            return Mathf.Clamp01(total);
        }
    }
}
