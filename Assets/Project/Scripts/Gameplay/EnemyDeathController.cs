using System;
using System.Collections;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class EnemyDeathController : MonoBehaviour
    {
        private readonly List<SpriteRenderer> _renderers = new();
        private readonly List<Color> _originalColors = new();
        private bool _started;

        public bool IsPlaying => _started;

        public bool Begin(
            EnemyData enemy,
            EnemyDeathSettings settings,
            Action spawnDrops)
        {
            if (_started || enemy == null)
                return false;

            _started = true;
            settings ??= new EnemyDeathSettings();
            StopCombatAndCapturePresentation();
            StartCoroutine(Play(enemy, settings, spawnDrops));
            return true;
        }

        private void StopCombatAndCapturePresentation()
        {
            AiNodeRunner runner = GetComponent<AiNodeRunner>();
            if (runner != null)
                runner.enabled = false;

            EnemyAttackController attacks =
                GetComponent<EnemyAttackController>();
            if (attacks != null)
                attacks.enabled = false;

            foreach (Collider2D collider in
                     GetComponentsInChildren<Collider2D>(true))
                collider.enabled = false;

            foreach (SpriteRenderer renderer in
                     GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer.GetComponent<FalseHeightVisual>() != null ||
                    renderer.gameObject.name == "Ground Shadow")
                    continue;
                _renderers.Add(renderer);
                _originalColors.Add(renderer.color);
            }
        }

        private IEnumerator Play(
            EnemyData enemy,
            EnemyDeathSettings settings,
            Action spawnDrops)
        {
            yield return Flash(settings.flashDuration, settings.flashCadence);

            FalseHeightController height =
                GetComponent<FalseHeightController>();
            float landingDeadline =
                Time.time + Mathf.Max(0f, settings.maximumLandingWait);
            while (height != null && height.IsAirborne &&
                   Time.time < landingDeadline)
                yield return null;

            yield return FadeTint(Color.black, settings.fadeToBlackDuration);
            SpawnPoof(enemy, settings);
            spawnDrops?.Invoke();
            yield return FadeOut(height, settings.fadeOutDuration);
            Destroy(gameObject);
        }

        private IEnumerator Flash(float duration, float cadence)
        {
            float end = Time.time + Mathf.Max(0f, duration);
            float interval = Mathf.Max(0.01f, cadence);
            bool red = true;
            while (Time.time < end)
            {
                SetTint(red ? Color.red : Color.white);
                red = !red;
                float next = Mathf.Min(end, Time.time + interval);
                while (Time.time < next)
                    yield return null;
            }
            SetTint(Color.white);
        }

        private IEnumerator FadeTint(Color target, float duration)
        {
            Color[] starting = CaptureCurrentColors();
            float length = Mathf.Max(0f, duration);
            if (length <= 0f)
            {
                SetTint(target);
                yield break;
            }

            float start = Time.time;
            while (Time.time - start < length)
            {
                float t = Mathf.Clamp01((Time.time - start) / length);
                for (int i = 0; i < _renderers.Count; i++)
                {
                    if (_renderers[i] == null)
                        continue;
                    Color color = Color.Lerp(starting[i], target, t);
                    color.a = _originalColors[i].a;
                    _renderers[i].color = color;
                }
                yield return null;
            }
            SetTint(target);
        }

        private IEnumerator FadeOut(
            FalseHeightController height,
            float duration)
        {
            Color[] starting = CaptureCurrentColors();
            float length = Mathf.Max(0f, duration);
            float start = Time.time;
            do
            {
                float t = length <= 0f
                    ? 1f
                    : Mathf.Clamp01((Time.time - start) / length);
                for (int i = 0; i < _renderers.Count; i++)
                {
                    if (_renderers[i] == null)
                        continue;
                    Color color = starting[i];
                    color.a = Mathf.Lerp(starting[i].a, 0f, t);
                    _renderers[i].color = color;
                }
                height?.SetShadowAlpha(1f - t);
                if (t >= 1f)
                    break;
                yield return null;
            } while (true);
        }

        private void SetTint(Color tint)
        {
            for (int i = 0; i < _renderers.Count; i++)
            {
                if (_renderers[i] == null)
                    continue;
                Color color = tint;
                color.a = _originalColors[i].a;
                _renderers[i].color = color;
            }
        }

        private Color[] CaptureCurrentColors()
        {
            Color[] colors = new Color[_renderers.Count];
            for (int i = 0; i < _renderers.Count; i++)
                colors[i] = _renderers[i] != null
                    ? _renderers[i].color
                    : Color.clear;
            return colors;
        }

        private void SpawnPoof(
            EnemyData enemy,
            EnemyDeathSettings settings)
        {
            GameObject prefab = ResolvePoofPrefab(enemy, settings);
            if (prefab == null)
                return;

            GameObject instance = Instantiate(
                prefab, transform.position, Quaternion.identity);
            float lifetime = 0.1f;
            foreach (ParticleSystem particle in
                     instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = particle.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                lifetime = Mathf.Max(
                    lifetime,
                    main.duration + main.startLifetime.constantMax);
                particle.Play(true);
            }
            Destroy(instance, lifetime);
        }

        public static GameObject ResolvePoofPrefab(
            EnemyData enemy,
            EnemyDeathSettings settings)
        {
            if (enemy == null)
                return null;
            return enemy.poofMode switch
            {
                EnemyPoofMode.Disabled => null,
                EnemyPoofMode.Override => enemy.deathPoofPrefab,
                _ => settings?.defaultPoofPrefab
            };
        }
    }
}
