using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.TimeAndWeather
{
    /// <summary>Camera shake and telegraphed falling-rock hazards for earthquakes.</summary>
    public sealed class EarthquakeWeatherController : ITickable, ILateTickable,
        IDisposable
    {
        public const string EffectId = "hazard.earthquake";

        private readonly IRegionalWeatherService _weather;
        private readonly IIndoorWeatherMask _indoorMask;
        private readonly PlayerDataController _player;
        private Camera _camera;
        private Vector3 _cameraRestPosition;
        private bool _wasShaking;
        private float _intensity;
        private float _nextRockTime;

        public EarthquakeWeatherController(
            IRegionalWeatherService weather,
            IIndoorWeatherMask indoorMask,
            PlayerDataController player)
        {
            _weather = weather;
            _indoorMask = indoorMask;
            _player = player;
        }

        public void Tick()
        {
            Vector2 playerPosition = _player.transform.position;
            WeatherSample sample = _weather.Sample(playerPosition);
            _intensity = GetEarthquakeIntensity(sample);

            if (_intensity <= 0f || _indoorMask?.IsViewerIndoors == true)
            {
                _nextRockTime = 0f;
                return;
            }

            if (_nextRockTime <= 0f)
                _nextRockTime = Time.time + UnityEngine.Random.Range(0.25f, 0.7f);

            if (Time.time < _nextRockTime)
                return;

            SpawnRock(playerPosition, _intensity);
            _nextRockTime = Time.time + UnityEngine.Random.Range(0.55f, 1.15f) /
                Mathf.Lerp(0.75f, 1.25f, _intensity);
        }

        public void LateTick()
        {
            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null)
                    return;
                _cameraRestPosition = _camera.transform.localPosition;
            }

            bool shake = _intensity > 0f &&
                         _indoorMask?.IsViewerIndoors != true;
            if (shake)
            {
                if (!_wasShaking)
                    _cameraRestPosition = _camera.transform.localPosition;

                float strength = Mathf.Lerp(0.035f, 0.16f, _intensity);
                float x = (Mathf.PerlinNoise(0f, Time.unscaledTime * 27f) - .5f) * 2f;
                float y = (Mathf.PerlinNoise(19f, Time.unscaledTime * 31f) - .5f) * 2f;
                _camera.transform.localPosition = _cameraRestPosition +
                                                  new Vector3(x, y, 0f) * strength;
            }
            else if (_wasShaking)
            {
                _camera.transform.localPosition = _cameraRestPosition;
            }

            _wasShaking = shake;
        }

        public void Dispose()
        {
            if (_wasShaking && _camera != null)
                _camera.transform.localPosition = _cameraRestPosition;
        }

        private void SpawnRock(Vector2 playerPosition, float intensity)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle *
                             Mathf.Lerp(2.5f, 7f, UnityEngine.Random.value);
            Vector2 impactPosition = playerPosition + offset;
            if (_indoorMask?.IsWorldPositionIndoors(impactPosition) == true)
                return;

            GameObject hazard = new("Earthquake Falling Rock");
            hazard.transform.position = impactPosition;
            hazard.AddComponent<EarthquakeFallingRock>().Initialize(
                _player,
                Mathf.Lerp(0.9f, 0.6f, intensity),
                Mathf.Lerp(0.65f, 0.9f, intensity),
                Mathf.RoundToInt(Mathf.Lerp(8f, 16f, intensity)));
        }

        public static float GetEarthquakeIntensity(WeatherSample sample)
        {
            foreach (WeatherEffectSample active in
                     sample.ActiveEffects ?? Array.Empty<WeatherEffectSample>())
            {
                if (active.Effect != null &&
                    string.Equals(active.Effect.EffectId, EffectId,
                        StringComparison.OrdinalIgnoreCase))
                    return active.Intensity;
            }
            return 0f;
        }
    }

    public sealed class EarthquakeFallingRock : MonoBehaviour
    {
        private static Sprite _discSprite;
        private PlayerDataController _player;
        private Transform _shadow;
        private Transform _rock;
        private float _telegraphSeconds;
        private float _radius;
        private int _damage;
        private float _age;
        private bool _impacted;

        public void Initialize(PlayerDataController player, float telegraphSeconds,
            float radius, int damage)
        {
            _player = player;
            _telegraphSeconds = telegraphSeconds;
            _radius = radius;
            _damage = damage;

            _shadow = CreateDisc("Growing Shadow", new Color(0f, 0f, 0f, .52f), -1);
            _shadow.localScale = Vector3.one * .08f;
            _rock = CreateDisc("Rock", new Color(.25f, .20f, .16f, 1f), 5);
            _rock.localPosition = Vector3.up * 5f;
            _rock.localScale = Vector3.one * (_radius * 1.25f);
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (!_impacted)
            {
                float progress = Mathf.Clamp01(_age / _telegraphSeconds);
                _shadow.localScale = Vector3.one *
                    Mathf.Lerp(.08f, _radius * 2f, progress);
                _rock.localPosition = Vector3.up * Mathf.Lerp(5f, 0f,
                    progress * progress);
                if (progress >= 1f)
                    Impact();
                return;
            }

            float fade = 1f - Mathf.Clamp01((_age - _telegraphSeconds) / .28f);
            _rock.localScale = Vector3.one * (_radius * 1.25f * fade);
            if (fade <= 0f)
                Destroy(gameObject);
        }

        private void Impact()
        {
            _impacted = true;
            _shadow.gameObject.SetActive(false);
            if (_player != null && Vector2.Distance(transform.position,
                    _player.transform.position) <= _radius)
            {
                IDamageable damageable = _player.GetComponent<IDamageable>();
                damageable?.TakeDamage(new AttackContext(
                    gameObject, null, _damage, EntityDamageSource.Environment));
            }
        }

        private Transform CreateDisc(string objectName, Color color, int order)
        {
            GameObject child = new(objectName);
            child.transform.SetParent(transform, false);
            SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = GetDiscSprite();
            renderer.color = color;
            renderer.sortingOrder = order;
            return child.transform;
        }

        private static Sprite GetDiscSprite()
        {
            if (_discSprite != null)
                return _discSprite;

            const int size = 32;
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
            {
                name = "Earthquake Hazard Disc",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            Color[] pixels = new Color[size * size];
            Vector2 center = Vector2.one * ((size - 1) * .5f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center) /
                                 (size * .5f);
                pixels[x + y * size] = new Color(1f, 1f, 1f,
                    Mathf.Clamp01((1f - distance) * 5f));
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            _discSprite = Sprite.Create(texture, new Rect(0, 0, size, size),
                new Vector2(.5f, .5f), size);
            _discSprite.name = "Earthquake Hazard Disc";
            _discSprite.hideFlags = HideFlags.HideAndDontSave;
            return _discSprite;
        }
    }
}
