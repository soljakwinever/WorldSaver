using System.Collections;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerDataController))]
    public sealed class PlayerDeathSequenceController : MonoBehaviour,
        IMovementLock
    {
        private static readonly int DissolveAmount =
            Shader.PropertyToID("_DissolveAmount");
        private static readonly int EdgeIntensity =
            Shader.PropertyToID("_EdgeIntensity");
        private static readonly int VignetteProgress =
            Shader.PropertyToID("_Progress");

        [Header("Timing")]
        [SerializeField, Min(0f)] private float flashDuration = 0.55f;
        [SerializeField, Min(1)] private int flashCount = 6;
        [SerializeField, Min(0f)] private float fadeToBlackDuration = 0.6f;
        [SerializeField, Min(0f)] private float dissolveDuration = 0.6f;
        [SerializeField, Min(0f)] private float particleDuration = 0.45f;
        [SerializeField, Min(0f)] private float vignetteDuration = 0.9f;
        [SerializeField, Min(0f)] private float blackHoldDuration = 1f;
        [SerializeField, Min(0f)] private float fadeFromBlackDuration = 0.45f;

        [Header("Visuals")]
        [SerializeField] private Color flashColor = Color.red;
        [SerializeField, ColorUsage(true, true)]
        private Color dissolveEdgeColor = new(2f, 0.15f, 0.05f, 1f);
        [SerializeField, Min(0f)] private float hdrGlowIntensity = 2f;
        [SerializeField] private string respawnPrompt =
            "Press Attack to Respawn";

        [InjectOptional] private IInputManager _input;

        private PlayerDataController _player;
        private SpriteRenderer _sprite;
        private Material _originalMaterial;
        private Color _originalColor;
        private Material _dissolveMaterial;
        private Material _vignetteMaterial;
        private Coroutine _routine;
        private bool _waitingForAttack;
        private bool _respawned;
        private bool _visualsCaptured;
        private bool _freezeMovement;
        private float _overlayAlpha;
        private float _vignette;

        public bool IsRunning => _routine != null;
        public bool IsMovementLocked => _freezeMovement;

        private void Awake()
        {
            _player = GetComponent<PlayerDataController>();
            _sprite = GetComponentInChildren<SpriteRenderer>(true);
        }

        private void OnEnable()
        {
            if (_input != null)
                _input.InputPerformed += OnInput;
        }

        private void OnDisable()
        {
            if (_input != null)
                _input.InputPerformed -= OnInput;
            CleanupVisuals();
        }

        public bool Begin(bool wasSwallowed)
        {
            if (_routine != null || _player == null)
                return false;

            _routine = StartCoroutine(Run(wasSwallowed));
            return true;
        }

        private IEnumerator Run(bool wasSwallowed)
        {
            _originalMaterial = _sprite != null ? _sprite.sharedMaterial : null;
            _originalColor = _sprite != null ? _sprite.color : Color.white;
            _visualsCaptured = _sprite != null;

            if (!wasSwallowed)
            {
                IKnockbackState knockback = GetComponent<IKnockbackState>();
                while (knockback != null && knockback.IsLaunched)
                    yield return null;

                _freezeMovement = true;

                if (_sprite != null)
                {
                    float interval = flashDuration /
                                     Mathf.Max(1, flashCount * 2);
                    for (int i = 0; i < flashCount * 2; i++)
                    {
                        _sprite.color = i % 2 == 0
                            ? flashColor
                            : _originalColor;
                        yield return WaitUnscaled(interval);
                    }

                    yield return Animate(fadeToBlackDuration, value =>
                        _sprite.color = Color.Lerp(
                            _originalColor, Color.black, value));
                    PrepareDissolveMaterial();
                    yield return Animate(dissolveDuration, value =>
                    {
                        if (_dissolveMaterial != null)
                            _dissolveMaterial.SetFloat(DissolveAmount, value);
                        else
                            _sprite.color = new Color(0f, 0f, 0f, 1f - value);
                    });
                    SpawnParticles();
                    _sprite.enabled = false;
                    yield return WaitUnscaled(particleDuration);
                }
            }
            else
            {
                _freezeMovement = true;
            }

            yield return Animate(vignetteDuration, value =>
                _vignette = value);
            _vignette = 1f;
            _overlayAlpha = 1f;
            if (wasSwallowed)
                GetComponent<SwallowedStateController>()?.Release(false);
            yield return WaitUnscaled(blackHoldDuration);

            _waitingForAttack = true;
            _respawned = false;
            _player.Respawned += OnRespawned;
            while (!_respawned)
                yield return null;
            _player.Respawned -= OnRespawned;
            _waitingForAttack = false;

            RestoreSprite();
            _vignette = 0f;
            yield return Animate(fadeFromBlackDuration, value =>
                _overlayAlpha = 1f - value);
            _overlayAlpha = 0f;
            _freezeMovement = false;
            _routine = null;
        }

        private void OnInput(InputContext context)
        {
            if (_waitingForAttack && context.AttackPressed)
                _player.CompleteDeath();
        }

        private void OnRespawned(PlayerDataController _) => _respawned = true;

        private void PrepareDissolveMaterial()
        {
            Shader shader = Shader.Find("WorldSaver/Player Dissolve");
            if (shader == null || _sprite == null)
                return;
            _dissolveMaterial = new Material(shader);
            if (_sprite.sprite != null)
                _dissolveMaterial.mainTexture = _sprite.sprite.texture;
            _dissolveMaterial.SetColor("_EdgeColor", dissolveEdgeColor);
            _dissolveMaterial.SetFloat(EdgeIntensity, hdrGlowIntensity);
            _dissolveMaterial.SetFloat(DissolveAmount, 0f);
            _sprite.material = _dissolveMaterial;
            _sprite.color = Color.white;
        }

        private void SpawnParticles()
        {
            var host = new GameObject("Player Death Particles");
            host.transform.position = transform.position;
            ParticleSystem particles = host.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.duration = Mathf.Max(0.1f, particleDuration);
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.16f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                dissolveEdgeColor, Color.black);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, 28)
            });
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.35f;
            ParticleSystemRenderer renderer =
                host.GetComponent<ParticleSystemRenderer>();
            renderer.sortingOrder = _sprite != null
                ? _sprite.sortingOrder + 1
                : 2;
            particles.Play();
            Destroy(host, main.duration + 1f);
        }

        private IEnumerator Animate(float duration,
            System.Action<float> update)
        {
            if (duration <= 0f)
            {
                update(1f);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                update(Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
        }

        private static IEnumerator WaitUnscaled(float duration)
        {
            float end = Time.unscaledTime + Mathf.Max(0f, duration);
            while (Time.unscaledTime < end)
                yield return null;
        }

        private void OnGUI()
        {
            if (_vignette <= 0f && _overlayAlpha <= 0f &&
                !_waitingForAttack)
                return;

            int previousDepth = GUI.depth;
            GUI.depth = -10000;
            if (_vignette > 0f)
            {
                EnsureVignetteMaterial();
                if (_vignetteMaterial != null)
                {
                    _vignetteMaterial.SetFloat(VignetteProgress, _vignette);
                    Graphics.DrawTexture(
                        new Rect(0f, 0f, Screen.width, Screen.height),
                        Texture2D.whiteTexture, _vignetteMaterial);
                }
            }

            if (_overlayAlpha > 0f)
            {
                Color previous = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, _overlayAlpha);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height),
                    Texture2D.whiteTexture);
                GUI.color = previous;
            }

            if (_waitingForAttack)
            {
                GUIStyle style = new(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = Mathf.RoundToInt(Mathf.Clamp(
                        Screen.height * 0.035f, 18f, 40f)),
                    normal = { textColor = Color.white }
                };
                GUI.Label(new Rect(0f, Screen.height * 0.58f,
                    Screen.width, 60f), respawnPrompt, style);
            }
            GUI.depth = previousDepth;
        }

        private void EnsureVignetteMaterial()
        {
            if (_vignetteMaterial != null)
                return;
            Shader shader = Shader.Find("Hidden/WorldSaver/Death Vignette");
            if (shader != null)
                _vignetteMaterial = new Material(shader);
        }

        private void RestoreSprite()
        {
            if (_sprite == null || !_visualsCaptured)
                return;
            _sprite.sharedMaterial = _originalMaterial;
            _sprite.color = _originalColor;
            _sprite.enabled = true;
            if (_dissolveMaterial != null)
                Destroy(_dissolveMaterial);
            _dissolveMaterial = null;
            _visualsCaptured = false;
        }

        private void CleanupVisuals()
        {
            _waitingForAttack = false;
            if (_player != null)
                _player.Respawned -= OnRespawned;
            RestoreSprite();
            if (_vignetteMaterial != null)
                Destroy(_vignetteMaterial);
            _vignetteMaterial = null;
            _vignette = 0f;
            _overlayAlpha = 0f;
            _freezeMovement = false;
            _routine = null;
        }
    }
}
