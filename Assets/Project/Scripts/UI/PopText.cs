using System;
using TMPro;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.UI
{
    public readonly struct PopTextOptions
    {
        public readonly Color Color;
        public readonly Material Material;
        public readonly Func<Color> ColorSelector;
        public readonly bool UseGravity;
        public readonly Vector2 InitialVelocity;
        public readonly float Gravity;
        public readonly float MaximumLifetime;

        public PopTextOptions(
            Color color,
            Material material = null,
            Func<Color> colorSelector = null,
            bool useGravity = true,
            Vector2? initialVelocity = null,
            float gravity = 9.81f,
            float maximumLifetime = 5f)
        {
            Color = color;
            Material = material;
            ColorSelector = colorSelector;
            UseGravity = useGravity;
            InitialVelocity = initialVelocity ??
                              (useGravity
                                  ? new Vector2(0f, 4.5f)
                                  : new Vector2(0f, 1.25f));
            Gravity = Mathf.Max(0f, gravity);
            MaximumLifetime = Mathf.Max(0.1f, maximumLifetime);
        }

        public static PopTextOptions Damage(Color color) => new(color, gravity: 36.4f);

        public static PopTextOptions Healing(Color color) =>
            new(color, useGravity: false, maximumLifetime: 1.5f);
    }

    [RequireComponent(typeof(TextMeshProUGUI))]
    public sealed class PopText : MonoBehaviour, IPooledVisualEffect
    {
        private const float OffscreenMargin = 48f;

        private TextMeshProUGUI _text;
        private Material _defaultMaterial;
        private IEffectSpawner _spawner;
        private Vector2 _velocity;
        private Func<Color> _colorSelector;
        private float _gravity;
        private float _maximumLifetime;
        private float _age;
        private bool _useGravity;
        private bool _playing;

        private void Awake()
        {
            _text = GetComponent<TextMeshProUGUI>();
            _text.alignment = TextAlignmentOptions.Center;
            _text.fontSize = 1;
            _text.fontStyle = FontStyles.Bold;
            _text.overflowMode = TextOverflowModes.Overflow;
            _text.enableWordWrapping = false;
            _text.raycastTarget = false;

            RectTransform rect = (RectTransform)transform;
            rect.sizeDelta = new Vector2(3f, 1f);
        }

        [Inject]
        public void Construct(PopTextSettings settings)
        {
            if (settings?.FontAsset != null)
                _text.font = settings.FontAsset;
            else if (_text.font == null)
                _text.font = TMP_Settings.defaultFontAsset;

            _defaultMaterial = _text.fontSharedMaterial;
        }

        public void Show(string value, PopTextOptions options)
        {
            _text.text = value ?? string.Empty;
            _text.color = options.Color;
            _text.fontSharedMaterial = options.Material != null
                ? options.Material
                : _defaultMaterial;
            _colorSelector = options.ColorSelector;
            _velocity = options.InitialVelocity;
            _gravity = options.Gravity;
            _maximumLifetime = options.MaximumLifetime;
            _useGravity = options.UseGravity;
            _age = 0f;
            _playing = true;
        }

        public void Show(
            string value,
            Color color,
            Material material = null,
            Func<Color> colorSelector = null,
            bool useGravity = true)
        {
            Show(value, new PopTextOptions(
                color,
                material,
                colorSelector,
                useGravity));
        }

        private void Update()
        {
            if (!_playing)
                return;

            float deltaTime = Time.deltaTime;
            _age += deltaTime;
            if (_colorSelector != null)
                _text.color = _colorSelector();

            if (_useGravity)
                _velocity.y -= _gravity * deltaTime;
            transform.position += (Vector3)(_velocity * deltaTime);

            Camera camera = Camera.main;
            Vector3 screenPosition = camera != null
                ? camera.WorldToScreenPoint(transform.position)
                : Vector3.zero;
            bool fellOffscreen = _useGravity && _velocity.y < 0f &&
                                 screenPosition.y < -OffscreenMargin;
            bool roseOffscreen = !_useGravity && camera != null &&
                                 screenPosition.y >
                                 Screen.height + OffscreenMargin;
            if (_age >= _maximumLifetime || fellOffscreen || roseOffscreen)
                _spawner?.Despawn(this);
        }

        public void OnEffectSpawned(IEffectSpawner spawner)
        {
            _spawner = spawner;
            _playing = false;
        }

        public void OnEffectDespawned()
        {
            _playing = false;
            _colorSelector = null;
            _text.text = string.Empty;
            _text.fontSharedMaterial = _defaultMaterial;
            _spawner = null;
        }
    }
}
