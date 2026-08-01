using System.Collections;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    /// <summary>Owns a one-shot destination preview and transition.</summary>
    public sealed class FastTravelPortal : MonoBehaviour
    {
        private Transform _player;
        private Rigidbody2D _playerBody;
        private IChunkLoader _chunkloader;
        private Camera _targetCamera;
        private RenderTexture _texture;
        private bool _ownsTexture;
        private float _distance;
        private float _duration;
        private Vector3 _destination;
        private Vector3 _initialScale;
        private float _whiteAlpha;
        private bool _travelling;
        private bool _ready;
        private float _creationDuration;
        private GameObject _startOneShot;
        private GameObject _readyOneShot;
        private float _particleSizeMultiplier;
        private readonly List<EmissionState> _emissions = new();
        private readonly List<Material> _maskMaterials = new();
        private readonly HashSet<ParticleSystemRenderer> _unmaskedParticles = new();
        private Material _destinationMaterial;
        private Sprite _destinationSprite;
        private Texture2D _destinationSpriteTexture;
        private SpriteRenderer _destinationRenderer;

        private readonly struct EmissionState
        {
            public readonly ParticleSystem Particle;
            public readonly float TimeMultiplier;
            public readonly float DistanceMultiplier;

            public EmissionState(ParticleSystem particle)
            {
                Particle = particle;
                ParticleSystem.EmissionModule emission = particle.emission;
                TimeMultiplier = emission.rateOverTimeMultiplier;
                DistanceMultiplier = emission.rateOverDistanceMultiplier;
            }
        }

        public void Initialize(
            EventPortalEffect effect,
            Transform player,
            IChunkLoader chunkloader)
        {
            _player = player;
            _playerBody = player != null ? player.GetComponent<Rigidbody2D>() : null;
            _chunkloader = chunkloader;
            _destination = effect.destinationWorldPosition;
            _distance = Mathf.Max(0.1f, effect.activationDistance);
            _duration = Mathf.Max(0.05f, effect.transitionDuration);
            _creationDuration = Mathf.Max(0.1f, effect.creationDuration);
            _startOneShot = effect.startOneShot;
            _readyOneShot = effect.readyOneShot;
            _particleSizeMultiplier = Mathf.Max(0.1f, effect.particleSizeMultiplier);
            _initialScale = transform.localScale;

            CreateOuterRim(effect.outerRimEffect);

            _texture = effect.renderTexture;
            if (_texture == null)
            {
                int size = Mathf.Clamp(effect.textureSize, 32, 1024);
                _texture = new RenderTexture(size, size, 16)
                {
                    name = $"Portal Preview ({GetEntityId()})"
                };
                _texture.Create();
                _ownsTexture = true;
            }
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _texture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = previous;

            GameObject target = new("Portal Target");
            target.transform.SetParent(transform, false);
            target.transform.position = new Vector3(_destination.x, _destination.y, -10f);
            _targetCamera = target.AddComponent<Camera>();
            _targetCamera.orthographic = true;
            _targetCamera.orthographicSize = 6f;
            _targetCamera.targetTexture = _texture;
            _targetCamera.clearFlags = CameraClearFlags.SolidColor;
            _targetCamera.backgroundColor = Color.black;
            _targetCamera.enabled = false;

            ConfigureParticleMask();
            _chunkloader?.SetPortalPreview(_destination, true);
            CacheAndClearEmission();
            SpawnOneShot(_startOneShot);
            StartCoroutine(BuildPortal());
        }

        private void Update()
        {
            if (!_ready || _travelling || _player == null)
                return;
            if (((Vector2)_player.position - (Vector2)transform.position).sqrMagnitude <=
                _distance * _distance)
                StartCoroutine(Travel());
        }

        private IEnumerator BuildPortal()
        {
            float elapsed = 0f;
            while (elapsed < _creationDuration)
            {
                elapsed += Time.deltaTime;
                SetEmission(Mathf.Clamp01(elapsed / _creationDuration));
                yield return null;
            }

            SetEmission(1f);
            if (_targetCamera != null)
                _targetCamera.enabled = true;
            ApplyDestinationTexture();
            SpawnOneShot(_readyOneShot);
            _ready = true;
        }

        private void CacheAndClearEmission()
        {
            _emissions.Clear();
            foreach (ParticleSystem particle in GetComponentsInChildren<ParticleSystem>(true))
            {
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ParticleSystem.MainModule main = particle.main;
                ParticleSystemRenderer renderer =
                    particle.GetComponent<ParticleSystemRenderer>();
                if (renderer == null || !_unmaskedParticles.Contains(renderer))
                    main.startSizeMultiplier *= _particleSizeMultiplier;
                _emissions.Add(new EmissionState(particle));
                ParticleSystem.EmissionModule emission = particle.emission;
                emission.rateOverTimeMultiplier = 0f;
                emission.rateOverDistanceMultiplier = 0f;
                particle.Play();
            }
        }

        private void CreateOuterRim(GameObject prefab)
        {
            if (prefab == null)
                return;
            GameObject instance = Instantiate(prefab, transform, false);
            instance.name = "Outer Rim";
            foreach (ParticleSystemRenderer renderer in
                     instance.GetComponentsInChildren<ParticleSystemRenderer>(true))
                _unmaskedParticles.Add(renderer);
        }

        private void SetEmission(float amount)
        {
            foreach (EmissionState state in _emissions)
            {
                if (state.Particle == null)
                    continue;
                ParticleSystem.EmissionModule emission = state.Particle.emission;
                emission.rateOverTimeMultiplier = state.TimeMultiplier * amount;
                emission.rateOverDistanceMultiplier = state.DistanceMultiplier * amount;
            }
        }

        private void SpawnOneShot(GameObject prefab)
        {
            if (prefab != null)
            {
                GameObject instance = Instantiate(
                    prefab,
                    transform.position,
                    transform.rotation);
                Destroy(instance, GetOneShotLifetime(instance));
            }
        }

        private static float GetOneShotLifetime(GameObject instance)
        {
            const float fallbackLifetime = 5f;
            float lifetime = 0f;
            foreach (ParticleSystem particle in
                     instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = particle.main;
                lifetime = Mathf.Max(
                    lifetime,
                    main.startDelay.constantMax +
                    main.duration +
                    main.startLifetime.constantMax);
            }
            foreach (AudioSource audio in
                     instance.GetComponentsInChildren<AudioSource>(true))
            {
                if (audio.clip != null)
                    lifetime = Mathf.Max(lifetime, audio.clip.length);
            }
            foreach (Animator animator in
                     instance.GetComponentsInChildren<Animator>(true))
            {
                RuntimeAnimatorController controller = animator.runtimeAnimatorController;
                if (controller == null)
                    continue;
                foreach (AnimationClip clip in controller.animationClips)
                    if (clip != null)
                        lifetime = Mathf.Max(lifetime, clip.length);
            }

            return (lifetime > 0f ? lifetime : fallbackLifetime) + 0.25f;
        }

        private IEnumerator Travel()
        {
            _travelling = true;
            float elapsed = 0f;
            while (elapsed < _duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / _duration);
                transform.localScale = Vector3.Lerp(_initialScale, _initialScale * 4f, t);
                _whiteAlpha = t;
                yield return null;
            }

            if (_playerBody != null)
            {
                _playerBody.linearVelocity = Vector2.zero;
                _playerBody.angularVelocity = 0f;
                _playerBody.position = _destination;
            }
            else if (_player != null)
                _player.position = _destination;

            DisableTarget();
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            foreach (Collider2D portalCollider in GetComponentsInChildren<Collider2D>(true))
                portalCollider.enabled = false;

            elapsed = 0f;
            while (elapsed < _duration)
            {
                elapsed += Time.unscaledDeltaTime;
                _whiteAlpha = 1f - Mathf.Clamp01(elapsed / _duration);
                yield return null;
            }
            _whiteAlpha = 0f;
            gameObject.SetActive(false);
        }

        private void ConfigureParticleMask()
        {
            Shader maskShader = Resources.Load<Shader>("PortalParticleMask");
            if (maskShader == null)
            {
                Debug.LogError("Portal particle-mask shader could not be loaded from Resources.");
                return;
            }

            int highestParticleOrder = int.MinValue;
            foreach (ParticleSystemRenderer particles in
                     GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (_unmaskedParticles.Contains(particles))
                    continue;
                Material mask = new(maskShader)
                {
                    name = "Portal Particle Mask (Runtime)"
                };
                if (particles.sharedMaterial != null &&
                    particles.sharedMaterial.mainTexture != null)
                    mask.mainTexture = particles.sharedMaterial.mainTexture;
                particles.material = mask;
                highestParticleOrder = Mathf.Max(
                    highestParticleOrder,
                    particles.sortingOrder);
                _maskMaterials.Add(mask);
            }

            _destinationRenderer = GetComponent<SpriteRenderer>();
            if (_destinationRenderer == null)
                _destinationRenderer = gameObject.AddComponent<SpriteRenderer>();
            if (_destinationRenderer.sprite == null)
            {
                _destinationSpriteTexture = new Texture2D(
                    Mathf.Max(1, _texture.width),
                    Mathf.Max(1, _texture.height),
                    TextureFormat.RGBA32,
                    false)
                {
                    name = "Portal Destination Geometry (Runtime)"
                };
                _destinationSprite = Sprite.Create(
                    _destinationSpriteTexture,
                    new Rect(
                        0f,
                        0f,
                        _destinationSpriteTexture.width,
                        _destinationSpriteTexture.height),
                    new Vector2(0.5f, 0.5f),
                    16f);
                _destinationSprite.name = "Portal Destination Quad (Runtime)";
                _destinationRenderer.sprite = _destinationSprite;
            }
            _destinationRenderer.enabled = false;
            if (highestParticleOrder != int.MinValue)
                _destinationRenderer.sortingOrder = highestParticleOrder + 1;
        }

        private void ApplyDestinationTexture()
        {
            if (_destinationRenderer == null)
                return;
            Shader destinationShader = Resources.Load<Shader>("PortalDestination");
            if (destinationShader == null)
            {
                Debug.LogError("Portal destination shader could not be loaded from Resources.");
                return;
            }
            _destinationMaterial = new Material(destinationShader)
            {
                name = "Portal Destination (Runtime)"
            };
            _destinationRenderer.material = _destinationMaterial;

            MaterialPropertyBlock properties = new();
            properties.SetTexture("_MainTex", _texture);
            _destinationRenderer.SetPropertyBlock(properties);
            _destinationRenderer.enabled = true;
        }

        private void DisableTarget()
        {
            _chunkloader?.SetPortalPreview(_destination, false);
            if (_targetCamera != null)
                _targetCamera.gameObject.SetActive(false);
        }

        private void OnDisable() => DisableTarget();

        private void OnDestroy()
        {
            DisableTarget();
            if (_ownsTexture && _texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }
            foreach (Material material in _maskMaterials)
                if (material != null)
                    Destroy(material);
            if (_destinationMaterial != null)
                Destroy(_destinationMaterial);
            if (_destinationSprite != null)
                Destroy(_destinationSprite);
            if (_destinationSpriteTexture != null)
                Destroy(_destinationSpriteTexture);
        }

        private void OnGUI()
        {
            if (_whiteAlpha <= 0f)
                return;
            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, _whiteAlpha);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
