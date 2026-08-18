using System.Collections.Generic;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class FalseHeightController : MonoBehaviour, IHasShadow
    {
        [SerializeField] private ShadowSize shadowSize = ShadowSize.Medium;
        [SerializeField] private ShadowSpriteSettings shadowSettings;
        [SerializeField, Min(0f)] private float knockbackPeakHeight = 0.65f;
        [SerializeField, Min(0.01f)] private float knockbackDuration = 0.35f;

        private readonly List<RendererProxy> _proxies = new();
        private readonly HashSet<SpriteRenderer> _captured = new();
        private SpriteRenderer _shadow;
        private float _startHeight;
        private float _peakHeight;
        private float _elapsed;
        private float _duration;
        private float _jumpAscent;
        private float _jumpHover;
        private float _jumpDescent;
        private bool _phasedJump;
        private bool _presentationVisible = true;

        public ShadowSize ShadowSize => shadowSize;
        public float VisualHeight { get; private set; }
        public bool IsAirborne => _duration > 0f;
        public bool IsPhasedJump => _phasedJump && _duration > 0f;
        public bool PresentationVisible => _presentationVisible;

        private void Awake()
        {
            if (shadowSettings == null)
                shadowSettings = Resources.Load<ShadowSpriteSettings>(
                    "ShadowSpriteSettings");
            shadowSize = ResolveShadowSize();
            CreateShadow();
            BuildRendererProxies();
        }

        private ShadowSize ResolveShadowSize()
        {
            float width = 0f;
            foreach (SpriteRenderer renderer in
                     GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer.sprite != null)
                    width = Mathf.Max(width, renderer.bounds.size.x);
            }
            if (width <= 0.8f) return ShadowSize.Small;
            if (width >= 1.75f) return ShadowSize.Large;
            return shadowSize;
        }

        private void Update()
        {
            if (_duration <= 0f)
                return;

            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / _duration);
            if (_phasedJump)
            {
                if (_elapsed < _jumpAscent)
                    VisualHeight = Mathf.Lerp(
                        _startHeight,
                        _peakHeight,
                        Mathf.Clamp01(_elapsed / _jumpAscent));
                else if (_elapsed < _jumpAscent + _jumpHover)
                    VisualHeight = _peakHeight;
                else
                    VisualHeight = Mathf.Lerp(
                        _peakHeight,
                        0f,
                        Mathf.Clamp01(
                            (_elapsed - _jumpAscent - _jumpHover) /
                            _jumpDescent));
            }
            else
            {
                float arc = EvaluateArc(_peakHeight, t);
                VisualHeight = Mathf.Lerp(_startHeight, 0f, t) + arc;
            }
            if (t >= 1f)
            {
                VisualHeight = 0f;
                _duration = 0f;
                _phasedJump = false;
            }
        }

        private void LateUpdate()
        {
            CaptureNewRenderers();
            for (int i = 0; i < _proxies.Count; i++)
                _proxies[i].Sync(VisualHeight, _presentationVisible);
        }

        public void Launch(float peakHeight, float duration)
        {
            if (peakHeight <= 0f || duration <= 0f)
                return;
            _startHeight = VisualHeight;
            _peakHeight = peakHeight;
            _duration = duration;
            _elapsed = 0f;
            _phasedJump = false;
        }

        public bool BeginPhasedJump(
            float peakHeight,
            float ascentDuration,
            float hoverDuration,
            float descentDuration)
        {
            if (peakHeight <= 0f || ascentDuration <= 0f ||
                descentDuration <= 0f)
                return false;
            _startHeight = VisualHeight;
            _peakHeight = peakHeight;
            _jumpAscent = ascentDuration;
            _jumpHover = Mathf.Max(0f, hoverDuration);
            _jumpDescent = descentDuration;
            _duration = _jumpAscent + _jumpHover + _jumpDescent;
            _elapsed = 0f;
            _phasedJump = true;
            return true;
        }

        public static float EvaluateArc(float peakHeight, float normalizedTime)
        {
            float t = Mathf.Clamp01(normalizedTime);
            return 4f * Mathf.Max(0f, peakHeight) * t * (1f - t);
        }

        public void LaunchFromKnockback() =>
            Launch(knockbackPeakHeight, knockbackDuration);

        public void SetShadowAlpha(float alpha)
        {
            if (_shadow == null)
                return;
            Color color = _shadow.color;
            color.a = Mathf.Clamp01(alpha);
            _shadow.color = color;
        }

        public void SetPresentationVisible(bool visible)
        {
            _presentationVisible = visible;
            if (_shadow != null)
                _shadow.enabled = visible;
            for (int i = 0; i < _proxies.Count; i++)
                _proxies[i].SetPresentationVisible(visible);
        }

        public void CancelHeight()
        {
            VisualHeight = 0f;
            _duration = 0f;
            _phasedJump = false;
        }

        public static bool TryLaunch(GameObject entity, float height, float duration)
        {
            if (entity == null)
                return false;
            IHasShadow target = entity.GetComponentInParent<IHasShadow>() ??
                                entity.GetComponentInChildren<IHasShadow>();
            if (target == null)
                return false;
            target.Launch(height, duration);
            return true;
        }

        private void CreateShadow()
        {
            var shadowObject = new GameObject("Ground Shadow");
            shadowObject.transform.SetParent(transform, false);
            _shadow = shadowObject.AddComponent<SpriteRenderer>();
            _shadow.sprite = ResolveShadowSprite();
            _shadow.spriteSortPoint = SpriteSortPoint.Pivot;

            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
            int order = 0;
            int layer = 0;
            bool found = false;
            foreach (SpriteRenderer renderer in renderers)
            {
                if (renderer == _shadow) continue;
                if (!found || renderer.sortingOrder < order)
                {
                    order = renderer.sortingOrder;
                    layer = renderer.sortingLayerID;
                    found = true;
                }
            }
            _shadow.sortingLayerID = layer;
            _shadow.sortingOrder = order - 1;
        }

        private Sprite ResolveShadowSprite()
        {
            Sprite sprite = shadowSettings != null
                ? shadowSettings.GetShadow(shadowSize)
                : null;
            if (sprite == null)
                Debug.LogError(
                    $"Missing imported shadow sprite 'Shadows_{(int)shadowSize}'.",
                    this);
            return sprite;
        }

        private void BuildRendererProxies()
        {
            CaptureNewRenderers();
        }

        private void CaptureNewRenderers()
        {
            SpriteRenderer[] sources = GetComponentsInChildren<SpriteRenderer>(true);
            foreach (SpriteRenderer source in sources)
            {
                if (source == _shadow || source.GetComponent<FalseHeightVisual>() != null)
                    continue;
                if (source.GetComponentInParent<FalseHeightController>() != this)
                    continue;
                if (!_captured.Add(source))
                    continue;
                var visual = new GameObject(source.gameObject.name + " (Raised Visual)");
                visual.transform.SetParent(transform, false);
                visual.AddComponent<FalseHeightVisual>();
                SpriteRenderer proxy = visual.AddComponent<SpriteRenderer>();
                _proxies.Add(new RendererProxy(this, source, proxy));
                source.forceRenderingOff = true;
            }
        }

        private void OnDisable()
        {
            VisualHeight = 0f;
            _duration = 0f;
            _phasedJump = false;
        }

        private void OnDestroy()
        {
            foreach (RendererProxy proxy in _proxies)
                if (proxy.Source != null) proxy.Source.forceRenderingOff = false;
        }

        private sealed class RendererProxy
        {
            private readonly FalseHeightController _owner;
            public readonly SpriteRenderer Source;
            private readonly SpriteRenderer _proxy;

            public RendererProxy(
                FalseHeightController owner,
                SpriteRenderer source,
                SpriteRenderer proxy)
            {
                _owner = owner;
                Source = source;
                _proxy = proxy;
            }

            public void Sync(float height, bool presentationVisible)
            {
                if (Source == null || _proxy == null) return;
                if (Source.GetComponentInParent<FalseHeightController>() != _owner)
                {
                    _proxy.enabled = false;
                    return;
                }
                Transform target = _proxy.transform;
                target.position = Source.transform.position + Vector3.up * height;
                target.rotation = Source.transform.rotation;
                Vector3 parentScale = target.parent.lossyScale;
                Vector3 sourceScale = Source.transform.lossyScale;
                target.localScale = new Vector3(
                    Divide(sourceScale.x, parentScale.x),
                    Divide(sourceScale.y, parentScale.y),
                    Divide(sourceScale.z, parentScale.z));
                _proxy.enabled = presentationVisible && Source.enabled;
                _proxy.sprite = Source.sprite;
                _proxy.color = Source.color;
                _proxy.flipX = Source.flipX;
                _proxy.flipY = Source.flipY;
                _proxy.sharedMaterials = Source.sharedMaterials;
                _proxy.sortingLayerID = Source.sortingLayerID;
                _proxy.sortingOrder = Source.sortingOrder;
                _proxy.maskInteraction = Source.maskInteraction;
            }

            public void SetPresentationVisible(bool visible)
            {
                if (_proxy != null)
                    _proxy.enabled = visible && Source != null && Source.enabled &&
                        Source.GetComponentInParent<FalseHeightController>() == _owner;
            }

            private static float Divide(float value, float divisor) =>
                Mathf.Approximately(divisor, 0f) ? value : value / divisor;
        }
    }

    public sealed class FalseHeightVisual : MonoBehaviour { }

}
