using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    public sealed class ScreenShakeService : MonoBehaviour,
        IScreenShakeService, ILateTickable, IDisposable
    {
        private sealed class ActiveShake
        {
            public ScreenShakeRequest Request;
            public float StartedAt;
        }

        private readonly List<ActiveShake> _active = new();
        private readonly Dictionary<string, ScreenShakeRequest> _continuous =
            new(StringComparer.Ordinal);
        private ScreenShakeSettings _settings;
        private Camera _camera;
        private Vector3 _appliedOffset;

        [Inject]
        public void Construct(ScreenShakeSettings settings) =>
            _settings = settings;

        public void Shake(ScreenShakeRequest request)
        {
            if (request.amplitude <= 0f || request.duration <= 0f)
                return;
            _active.Add(new ActiveShake
            {
                Request = request,
                StartedAt = Time.unscaledTime
            });
        }

        public void SetContinuous(string channel, ScreenShakeRequest request)
        {
            if (string.IsNullOrWhiteSpace(channel))
                throw new ArgumentException("A shake channel is required.",
                    nameof(channel));
            if (request.amplitude <= 0f)
            {
                _continuous.Remove(channel);
                return;
            }
            _continuous[channel] = request;
        }

        public void ClearContinuous(string channel)
        {
            if (!string.IsNullOrWhiteSpace(channel))
                _continuous.Remove(channel);
        }

        public void LateTick()
        {
            RemoveAppliedOffset();
            Camera main = Camera.main;
            if (main == null)
            {
                _camera = null;
                return;
            }
            _camera = main;

            float now = Time.unscaledTime;
            float amplitude = 0f;
            float frequency = 0f;
            for (int index = _active.Count - 1; index >= 0; index--)
            {
                ActiveShake active = _active[index];
                float duration = Mathf.Max(0.0001f, active.Request.duration);
                float progress = (now - active.StartedAt) / duration;
                if (progress >= 1f)
                {
                    _active.RemoveAt(index);
                    continue;
                }
                float envelope = Mathf.Lerp(
                    1f,
                    1f - Mathf.Clamp01(progress),
                    Mathf.Clamp01(active.Request.falloff));
                amplitude = Mathf.Max(
                    amplitude,
                    active.Request.amplitude * envelope);
                frequency = Mathf.Max(
                    frequency,
                    Mathf.Max(0.01f, active.Request.frequency));
            }

            foreach (ScreenShakeRequest request in _continuous.Values)
            {
                amplitude = Mathf.Max(amplitude, request.amplitude);
                frequency = Mathf.Max(
                    frequency,
                    Mathf.Max(0.01f, request.frequency));
            }

            float maximum = _settings != null
                ? Mathf.Max(0f, _settings.maximumAmplitude)
                : 0.35f;
            amplitude = Mathf.Min(amplitude, maximum);
            if (amplitude <= 0f)
                return;

            float sample = now * Mathf.Max(0.01f, frequency);
            float x = (Mathf.PerlinNoise(11.7f, sample) - 0.5f) * 2f;
            float y = (Mathf.PerlinNoise(37.1f, sample + 19.3f) - 0.5f) * 2f;
            Vector2 direction = Vector2.ClampMagnitude(new Vector2(x, y), 1f);
            _appliedOffset = (Vector3)(direction * amplitude);
            _camera.transform.localPosition += _appliedOffset;
        }

        private void RemoveAppliedOffset()
        {
            if (_camera != null && _appliedOffset.sqrMagnitude > 0f)
                _camera.transform.localPosition -= _appliedOffset;
            _appliedOffset = Vector3.zero;
        }

        public void Dispose()
        {
            RemoveAppliedOffset();
            _active.Clear();
            _continuous.Clear();
        }

        private void OnDisable() => RemoveAppliedOffset();
    }

    public sealed class PlayerDamageShakeResponder : IInitializable, IDisposable
    {
        private readonly PlayerDataController _player;
        private readonly IScreenShakeService _shake;
        private readonly ScreenShakeSettings _settings;
        private PersistentHealth _health;
        private int _previousHealth;
        private int _previousMaximumHealth;

        public PlayerDamageShakeResponder(
            PlayerDataController player,
            IScreenShakeService shake,
            ScreenShakeSettings settings)
        {
            _player = player;
            _shake = shake;
            _settings = settings;
        }

        public void Initialize()
        {
            _health = _player != null
                ? _player.GetComponent<PersistentHealth>()
                : null;
            if (_health == null)
                return;
            _previousHealth = _health.Health;
            _previousMaximumHealth = _health.MaxHealth;
            _health.HealthChanged += OnHealthChanged;
        }

        private void OnHealthChanged(int health, int maximumHealth)
        {
            bool maximumChanged = maximumHealth != _previousMaximumHealth;
            int damage = maximumChanged
                ? 0
                : Mathf.Max(0, _previousHealth - health);
            _previousHealth = health;
            _previousMaximumHealth = maximumHealth;
            if (damage <= 0 || maximumHealth <= 0)
                return;

            float maximumDamageRatio = _settings != null
                ? Mathf.Max(0.001f, _settings.damageForMaximumShake)
                : 0.25f;
            float normalized = Mathf.Clamp01(
                damage / (float)maximumHealth / maximumDamageRatio);
            float response = Mathf.Sqrt(normalized);
            float minimumAmplitude = _settings?.minimumDamageAmplitude ?? 0.035f;
            float maximumAmplitude = _settings?.maximumDamageAmplitude ?? 0.14f;
            float minimumDuration = _settings?.minimumDamageDuration ?? 0.08f;
            float maximumDuration = _settings?.maximumDamageDuration ?? 0.18f;
            float frequency = _settings?.damageFrequency ?? 34f;
            _shake?.Shake(new ScreenShakeRequest(
                Mathf.Lerp(minimumAmplitude, maximumAmplitude, response),
                Mathf.Lerp(minimumDuration, maximumDuration, response),
                frequency));
        }

        public void Dispose()
        {
            if (_health != null)
                _health.HealthChanged -= OnHealthChanged;
        }
    }
}
